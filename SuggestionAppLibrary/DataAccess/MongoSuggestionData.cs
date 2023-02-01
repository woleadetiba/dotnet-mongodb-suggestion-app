using System;
using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess
{
    public class MongoSuggestionData : ISuggestionData
    {
        private readonly IDbConnection _db;
        private readonly IUserData userData;
        private readonly IMemoryCache cache;

        private readonly IMongoCollection<SuggestionModel> _suggestioons;

        private const string CacheName = "SuggestyionName";


        public MongoSuggestionData(IDbConnection db, IUserData userData, IMemoryCache cache)
        {
            this._db = db;
            this.userData = userData;
            this.cache = cache;

            _suggestioons = db.SuggestionCollection;
        }

        public async Task<List<SuggestionModel>> GetAllSuggestions()
        {
            var output = this.cache.Get<List<SuggestionModel>>(CacheName);

            if (output is null)
            {
                var result = await _suggestioons.FindAsync(s => s.Archived == false);
                output = result.ToList();

                this.cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
            }


            return output;
        }

        public async Task<List<SuggestionModel>> GetAllApprovedSuggestions()
        {
            var output = await GetAllSuggestions();
            return output.Where(x => x.ApprovedForRelease).ToList();
        }

        public async Task<SuggestionModel> GetSuggestion(string id)
        {
            var result = await _suggestioons.FindAsync(s => s.Id == id);
            return result.FirstOrDefault();
        }

        public async Task<List<SuggestionModel>> GetAllSuggestionsAwaitingApproval()
        {
            var output = await GetAllSuggestions();

            return output.Where(x =>
                x.ApprovedForRelease == false &&
                x.Rejected == false).ToList();
        }

        public async Task UpdateSuggestion(SuggestionModel suggestion)
        {
            await _suggestioons.ReplaceOneAsync(s => s.Id == suggestion.Id, suggestion);
            this.cache.Remove(CacheName);
        }

        public async Task UpvoteSuggestion(string suggestionId, string userId)
        {
            var client = _db.Client;

            using var session = await client.StartSessionAsync();
            session.StartTransaction();

            try
            {
                var db = client.GetDatabase(_db.DbName);
                var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
                var suggestion = (await suggestionInTransaction.FindAsync(s => s.Id == suggestionId)).First();
                bool isUpvote = suggestion.UserVotes.Add(userId);

                if (isUpvote == false)
                {
                    suggestion.UserVotes.Remove(userId);
                }

                await suggestionInTransaction.ReplaceOneAsync(s => s.Id == suggestionId, suggestion);

                var userInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
                var user = await userData.GetUser(suggestion.Author.Id);

                if (isUpvote)
                {
                    user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
                }
                else
                {
                    var suggestionToRemove = user.VotedOnSuggestions.Where(x => suggestion.Id == suggestionId).First();
                    user.VotedOnSuggestions.Remove(new BasicSuggestionModel(suggestion));
                }

                await userInTransaction.ReplaceOneAsync(u => u.Id == userId, user);

                await session.CommitTransactionAsync();

                this.cache.Remove(CacheName);

            }
            catch (Exception e)
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }


        public async Task CreateSuggestion(SuggestionModel suggestion)
        {
            var client = _db.Client;
            using var session = await client.StartSessionAsync();

            session.StartTransaction();

            try
            {
                var db = client.GetDatabase(_db.DbName);
                var suggestionInTransaction = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
                await suggestionInTransaction.InsertOneAsync(suggestion);

                var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
                var user = await this.userData.GetUser(suggestion.Author.Id);

                user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));

                await usersInTransaction.ReplaceOneAsync(u => u.Id == user.Id, user);

                await session.CommitTransactionAsync();


            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }

    }
}


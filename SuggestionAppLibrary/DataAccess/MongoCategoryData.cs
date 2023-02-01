using System;
using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess
{
    public class MongoCategoryData : ICategoryData
    {
        private readonly IMongoCollection<CategoryModel> _categories;
        private readonly IMemoryCache _cache;
        private const string CacheName = "CategoryData";

        public MongoCategoryData(IDbConnection db, IMemoryCache cache)
        {
            _categories = db.CategoryCollection;
            this._cache = cache;
        }

        public async Task<List<CategoryModel>> GetAllCategories()
        {
            var output = _cache.Get<List<CategoryModel>>(CacheName);

            if (output is null)
            {
                var result = await _categories.FindAsync(_ => true);
                output = result.ToList();

                _cache.Set(CacheName, output, TimeSpan.FromDays(1));
            }

            return output;
        }


        public Task CreateCategory(CategoryModel category)
        {
            return _categories.InsertOneAsync(category);
        }


    }

}


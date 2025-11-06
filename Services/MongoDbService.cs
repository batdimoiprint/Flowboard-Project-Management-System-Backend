using MongoDB.Driver;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Flowboard_Project_Management_System_Backend.Services
{
    public class MongoDbService
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoClient _client;

        // -------------------------------------------------------
        // ✅ Constructor with connection from DI or config
        // -------------------------------------------------------
        public MongoDbService()
        {
            // Read from environment variables (set via .env)
            var connectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
            var databaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME");
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            _client = new MongoClient(settings);
            _database = _client.GetDatabase(databaseName);
        }
        // -------------------------------------------------------
        // ✅ Get collection (public for reuse in controllers)
        // -------------------------------------------------------
        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            return _database.GetCollection<T>(collectionName);
        }

        // -------------------------------------------------------
        // ✅ Get all documents
        // -------------------------------------------------------
        public async Task<List<T>> GetAllAsync<T>(string collectionName)
        {
            var collection = GetCollection<T>(collectionName);
            return await collection.Find(Builders<T>.Filter.Empty).ToListAsync();
        }

        // -------------------------------------------------------
        // ✅ Get one document by ID (handles both ObjectId/string)
        // -------------------------------------------------------
        public async Task<T?> GetByIdAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            FilterDefinition<T> filter;

            if (ObjectId.TryParse(id, out var objectId))
                filter = Builders<T>.Filter.Eq("_id", objectId);
            else
                filter = Builders<T>.Filter.Eq("_id", id); // fallback for string IDs

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        // -------------------------------------------------------
        // ✅ Insert one document
        // -------------------------------------------------------
        public async Task InsertOneAsync<T>(string collectionName, T document)
        {
            var collection = GetCollection<T>(collectionName);
            await collection.InsertOneAsync(document);
        }

        // -------------------------------------------------------
        // ✅ Replace (PUT)
        // -------------------------------------------------------
        public async Task<ReplaceOneResult> ReplaceOneAsync<T>(string collectionName, string id, T updatedDocument)
        {
            var collection = GetCollection<T>(collectionName);
            FilterDefinition<T> filter;

            if (ObjectId.TryParse(id, out var objectId))
                filter = Builders<T>.Filter.Eq("_id", objectId);
            else
                filter = Builders<T>.Filter.Eq("_id", id);

            return await collection.ReplaceOneAsync(filter, updatedDocument);
        }

        // -------------------------------------------------------
        // ✅ Update (PATCH)
        // -------------------------------------------------------
        public async Task<UpdateResult> UpdateOneAsync<T>(
            string collectionName,
            FilterDefinition<T> filter,
            UpdateDefinition<T> update)
        {
            var collection = GetCollection<T>(collectionName);
            return await collection.UpdateOneAsync(filter, update);
        }

        // -------------------------------------------------------
        // ✅ Delete by ID
        // -------------------------------------------------------
        public async Task<DeleteResult> DeleteOneAsync<T>(string collectionName, string id)
        {
            var collection = GetCollection<T>(collectionName);
            FilterDefinition<T> filter;

            if (ObjectId.TryParse(id, out var objectId))
                filter = Builders<T>.Filter.Eq("_id", objectId);
            else
                filter = Builders<T>.Filter.Eq("_id", id);

            return await collection.DeleteOneAsync(filter);
        }

        // -------------------------------------------------------
        // ✅ Expose database and client (for debugging / reuse)
        // -------------------------------------------------------
        public IMongoDatabase GetDatabase() => _database;
        public IMongoClient GetClient() => _client;
    }
}

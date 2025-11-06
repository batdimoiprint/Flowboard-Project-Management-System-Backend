using MongoDB.Driver;

namespace Flowboard_Project_Management_System_Backend.Services
{
    public class MongoDbService
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;


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

        public IMongoDatabase GetDatabase() => _database;
    }
}

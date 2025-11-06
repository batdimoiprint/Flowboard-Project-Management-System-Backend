using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class Category
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; }
        public string CategoryName { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string CreatedBy { get; set; }
    }
}
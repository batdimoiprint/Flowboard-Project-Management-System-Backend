using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class Board
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; }
        public string BoardName { get; set; }
        public string Description { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
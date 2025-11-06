using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class Permission
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string ProjectId { get; set; }
        public string Role { get; set; }
        public Privileges Privileges { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class Privileges
    {
        public bool Create { get; set; }
        public bool Read { get; set; }
        public bool Update { get; set; }
        public bool Delete { get; set; }
    }
}
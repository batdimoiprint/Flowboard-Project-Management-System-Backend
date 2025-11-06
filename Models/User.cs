using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class User
    {
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; } // Nullable so not required in POST

        public string UserName { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string MiddleName { get; set; }
        public string ContactNumber { get; set; }
        public DateTime? BirthDate { get; set; }
        public byte[]? UserIMG { get; set; } // Optional
        public string Email { get; set; }
        public string Password { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
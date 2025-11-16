using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class User
    {
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; } // Nullable so not required in POST

        [BsonRequired]
        [Required]
        public string UserName { get; set; } = string.Empty;

        [BsonRequired]
        [Required]
        public string LastName { get; set; } = string.Empty;

        [BsonRequired]
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [BsonRequired]
        [Required]
        public string MiddleName { get; set; } = string.Empty;

        [BsonRequired]
        [Required]
        public string ContactNumber { get; set; } = string.Empty;
        [BsonRequired]
        [Required]        
        public DateTime? BirthDate { get; set; }
        
        public byte[]? UserIMG { get; set; } = null; // Optional
            [BsonRequired]
        [Required]
        public string Email { get; set; } = string.Empty;
            [BsonRequired]
        [Required]
        public string Password { get; set; } = string.Empty;
            [BsonRequired]
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
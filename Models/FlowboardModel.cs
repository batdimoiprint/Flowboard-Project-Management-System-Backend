using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Flowboard_Project_Management_System_Backend.Models
{
    /// <summary>
    /// Centralized container for all Flowboard domain models to simplify imports across the backend.
    /// </summary>
    public static class FlowboardModel
    {
        /// <summary>
        /// Philippine Standard Geographic Code (PSGC) based address model
        /// </summary>
        public class Address
        {
            public string Region { get; set; } = string.Empty;
            public string RegionCode { get; set; } = string.Empty;
            public string Province { get; set; } = string.Empty;
            public string ProvinceCode { get; set; } = string.Empty;
            public string CityMunicipality { get; set; } = string.Empty;
            public string CityMunicipalityCode { get; set; } = string.Empty;
            public string Barangay { get; set; } = string.Empty;
            public string BarangayCode { get; set; } = string.Empty;
            public string StreetAddress { get; set; } = string.Empty;
            public string ZipCode { get; set; } = string.Empty;
        }

        public class User
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

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

            public byte[]? UserIMG { get; set; }

            [BsonRequired]
            [Required]
            public string Email { get; set; } = string.Empty;

            public Address? Address { get; set; }

            public Address? SecondaryAddress { get; set; }

            public string? SecondaryContactNumber { get; set; }

            [BsonRequired]
            [Required]
            public string Password { get; set; } = string.Empty;

            [BsonRequired]
            [Required]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // User role: "Admin", "User" (default), or "Client"
            [BsonRepresentation(BsonType.String)]
            public string Role { get; set; } = "User";
        }

        public class LoginRequest
        {
            public string? UserNameOrEmail { get; set; }
            public string? Password { get; set; }
        }

        public class Project
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonElement("projectName")]
            public string ProjectName { get; set; } = string.Empty;

            [BsonElement("description")]
            public string? Description { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("createdBy")]
            [JsonIgnore]
            public string? CreatedBy { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("teamMembers")]
            public List<string> TeamMembers { get; set; } = new();

            [BsonElement("permissions")]
            public Dictionary<string, string> Permissions { get; set; } = new();

            [BsonElement("createdAt")]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Board
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? ProjectId { get; set; }

            public string BoardName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;

            [BsonRepresentation(BsonType.ObjectId)]
            public string? CreatedBy { get; set; }

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Category
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? ProjectId { get; set; }

            public string CategoryName { get; set; } = string.Empty;

            [BsonRepresentation(BsonType.ObjectId)]
            public string? CreatedBy { get; set; }
        }

        public class Notification
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? UserId { get; set; }

            public string Message { get; set; } = string.Empty;
            public bool IsRead { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Permission
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? UserId { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? ProjectId { get; set; }

            public string Role { get; set; } = string.Empty;
            public Privileges Privileges { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Privileges
        {
            public bool Create { get; set; }
            public bool Read { get; set; }
            public bool Update { get; set; }
            public bool Delete { get; set; }
        }

        public class SubTask
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            // Parent MainTask reference
            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("mainTaskId")]
            public string? MainTaskId { get; set; }

            // Prefer storing a CategoryId reference to the Category document.
            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("categoryId")]
            public string? CategoryId { get; set; }

            // Backwards compatible: store the Category name if provided.
            public string? Category { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public string? ProjectId { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            public List<string> AssignedTo { get; set; } = new();

            [BsonRepresentation(BsonType.ObjectId)]
            public string? CreatedBy { get; set; }

            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? Priority { get; set; }

            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
            public DateTime? StartDate { get; set; }

            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
            public DateTime? EndDate { get; set; }

            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            public List<Comment> Comments { get; set; } = new();
        }

        public class MainTask
        {
            [BsonId]
            [BsonRepresentation(BsonType.ObjectId)]
            public string? Id { get; set; }

            public string? Title { get; set; }
            public string? Description { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("projectId")]
            public string? ProjectId { get; set; }

            [BsonRepresentation(BsonType.ObjectId)]
            [BsonElement("subTaskIds")]
            public List<string> SubTaskIds { get; set; } = new();

            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class Comment
        {
            [BsonRepresentation(BsonType.ObjectId)]
            public string? AuthorId { get; set; }

            public string? Content { get; set; }

            [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }
    }
}

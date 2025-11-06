using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class Task
    {
        // ----------------------------------------------------
        // 🆔 Primary Key (MongoDB ObjectId as string)
        // ----------------------------------------------------
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        // ----------------------------------------------------
        // 📦 Category & Assignment Info
        // ----------------------------------------------------
        [BsonRepresentation(BsonType.ObjectId)]
        public string? CategoryId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? AssignedTo { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string? CreatedBy { get; set; }

        // ----------------------------------------------------
        // 📝 Task Details
        // ----------------------------------------------------
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Priority { get; set; }   // e.g. Low, Medium, High
        public string? Status { get; set; }     // e.g. "to do", "in progress", "done"

        // ----------------------------------------------------
        // ⏰ Dates
        // ----------------------------------------------------
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? StartDate { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime? EndDate { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ----------------------------------------------------
        // 💬 Comments
        // ----------------------------------------------------
        public List<Comment>? Comments { get; set; } = new List<Comment>();
    }

    // --------------------------------------------------------
    // 💬 Embedded Comment Class (Nested Document)
    // --------------------------------------------------------
    public class Comment
    {
        [BsonRepresentation(BsonType.ObjectId)]
        public string? AuthorId { get; set; }

        public string? Content { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

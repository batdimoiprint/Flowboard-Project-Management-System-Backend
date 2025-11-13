using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json.Serialization;

namespace Flowboard_Project_Management_System_Backend.Models
{
    public class Project
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; } // ✅ make nullable so it's not required during creation

        [BsonElement("projectName")]
        public string ProjectName { get; set; } = null!; // ✅ ensure it's required logically but not by model validation

        [BsonElement("description")]
        public string? Description { get; set; }

        
         [BsonRepresentation(BsonType.ObjectId)]
        [BsonElement("createdBy")]
        [JsonIgnore] // ⬅️ Important: do not expect this in the JSON body
        public string? CreatedBy { get; set; }


        [BsonRepresentation(BsonType.ObjectId)]
        [BsonElement("teamMembers")]
        public List<string> TeamMembers { get; set; } = new(); // ✅ initialize list to avoid null

        // ✅ Added: Role-based permissions for each user in the project
        [BsonElement("permissions")]
        public Dictionary<string, string> Permissions { get; set; } = new(); // userId -> role (Owner, Editor, Viewer)

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // ✅ default timestamp
    }
}

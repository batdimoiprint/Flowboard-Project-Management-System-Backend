using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MongoDB.Bson;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/projects")]
    [Authorize] // JWT Protected
    public class ProjectsController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;

        public ProjectsController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        // 🔹 Helper: Extract user ID from JWT
        private string? GetUserIdFromToken()
        {
            if (User == null) return null;

            var userId =
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                User.FindFirst("id")?.Value ??
                User.FindFirst("userId")?.Value;

            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        // 🔹 Helper: Check if user can edit project
        private bool HasEditPermission(Project project, string userId)
        {
            if (project.Permissions == null) return false;
            if (!project.Permissions.TryGetValue(userId, out var role)) return false;
            return role == "Owner" || role == "Editor";
        }

        // 🔹 GET /api/projects
        [HttpGet]
        public IActionResult GetAll()
        {
            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");
            var projects = collection.Find(_ => true).ToList();
            return Ok(projects);
        }

        // 🔹 GET /api/projects/{id}
        [HttpGet("{id}", Name = "GetProjectById")]
        public IActionResult GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return NotFound(new { message = "Project not found." });

            return Ok(project);
        }

        // 🔹 POST /api/projects
        [HttpPost]
        public IActionResult Create([FromBody] Project project)
        {
            if (project == null)
                return BadRequest(new { message = "Project is required." });
            if (string.IsNullOrWhiteSpace(project.ProjectName))
                return BadRequest(new { message = "ProjectName is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");

            var userId = GetUserIdFromToken();
            if (userId == null)
                return Unauthorized(new { message = "Invalid user token." });

            project.Id = ObjectId.GenerateNewId().ToString();
            project.CreatedAt = DateTime.UtcNow;
            project.CreatedBy = userId; // ✅ assign automatically
            if (project.TeamMembers == null)
                project.TeamMembers = new List<string>();

            if (!project.TeamMembers.Contains(userId))
                project.TeamMembers.Add(userId); // Ensure creator is in team members

            // Assign Owner permissions to creator
            project.Permissions ??= new Dictionary<string, string>();
            project.Permissions[userId] = "Owner";

            collection.InsertOne(project);

            return CreatedAtRoute("GetProjectById", new { id = project.Id }, project);
        }

        // 🔹 PUT /api/projects/{id}
        [HttpPut("{id}")]
        public IActionResult Update(string id, [FromBody] Project updatedProject)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });
            if (updatedProject == null)
                return BadRequest(new { message = "Project is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");
            var existingProject = collection.Find(p => p.Id == id).FirstOrDefault();

            if (existingProject == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null || !HasEditPermission(existingProject, userId))
                return Forbid("You do not have permission to edit this project.");

            updatedProject.Id = id;
            updatedProject.CreatedAt = existingProject.CreatedAt;
            updatedProject.CreatedBy = existingProject.CreatedBy;
            updatedProject.Permissions = existingProject.Permissions;

            var result = collection.ReplaceOne(p => p.Id == id, updatedProject);

            return NoContent();
        }

        // 🔹 DELETE /api/projects/{id}
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null || !project.Permissions.ContainsKey(userId) || project.Permissions[userId] != "Owner")
                return Forbid("Only the project owner can delete this project.");

            collection.DeleteOne(p => p.Id == id);
            return NoContent();
        }

        // 🔹 PATCH /api/projects/{id}/permissions
        [HttpPatch("{id}/permissions")]
        public IActionResult UpdatePermission(string id, [FromBody] Dictionary<string, string> update)
        {
            if (!update.ContainsKey("userId") || !update.ContainsKey("role"))
                return BadRequest(new { message = "userId and role are required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null || !project.Permissions.ContainsKey(userId) || project.Permissions[userId] != "Owner")
                return Forbid("Only the project owner can update permissions.");

            project.Permissions[update["userId"]] = update["role"];
            collection.ReplaceOne(p => p.Id == id, project);

            return Ok(new { message = "Permissions updated." });
        }
    }
}

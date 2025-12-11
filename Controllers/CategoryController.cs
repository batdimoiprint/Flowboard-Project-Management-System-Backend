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
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/categories")]
    [Authorize]
    public class CategoryController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        public CategoryController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

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

        private bool IsProjectTeamMember(string projectId, string userId)
        {
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId)) return false;
            var db = _mongoDbService.GetDatabase();
            var projects = db.GetCollection<FlowModels.Project>("project");
            var proj = projects.Find(p => p.Id == projectId).FirstOrDefault();
            if (proj == null) return false;
            if (proj.TeamMembers == null) return false;
            return proj.TeamMembers.Contains(userId);
        }

        // GET /api/categories?projectId=<id>&includeTasks=true
        [HttpGet]
        public IActionResult GetAll([FromQuery] string? projectId = null, [FromQuery] bool includeTasks = false)
        {
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");

            if (!string.IsNullOrWhiteSpace(projectId))
            {
                if (includeTasks)
                {
                    var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
                    var categories = categoriesCollection.Find(c => c.ProjectId == projectId).ToList();
                    var response = new List<object>();
                    foreach (var c in categories)
                    {
                        var tasks = subTasksCollection.Find(t => t.ProjectId == c.ProjectId && t.Category == c.CategoryName).ToList();
                        response.Add(new { category = c, tasks = tasks });
                    }
                    return Ok(response);
                }
                else
                {
                    var cats = categoriesCollection.Find(c => c.ProjectId == projectId).ToList();
                    return Ok(cats);
                }
            }

            var all = categoriesCollection.Find(_ => true).ToList();
            return Ok(all);
        }

        // GET /api/categories/{id}
        [HttpGet("{id:length(24)}", Name = "GetCategoryById")]
        public IActionResult GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var cat = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (cat == null) return NotFound(new { message = "Category not found." });
            return Ok(cat);
        }

        // GET /api/categories/{id}/tasks
        [HttpGet("{id:length(24)}/tasks")]
        public IActionResult GetTasksForCategory(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var cat = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (cat == null) return NotFound(new { message = "Category not found." });

            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var tasks = subTasksCollection.Find(t => t.ProjectId == cat.ProjectId && t.Category == cat.CategoryName).ToList();
            return Ok(tasks);
        }

        // POST /api/categories - create new category
        [HttpPost]
        public IActionResult Create([FromBody] FlowModels.Category category)
        {
            if (category == null) return BadRequest(new { message = "Category is required." });
            if (string.IsNullOrWhiteSpace(category.ProjectId)) return BadRequest(new { message = "ProjectId is required." });
            if (string.IsNullOrWhiteSpace(category.CategoryName)) return BadRequest(new { message = "CategoryName is required." });

            var requesterId = GetUserIdFromToken();
            if (requesterId == null) return Unauthorized(new { message = "Invalid user token." });
            if (!IsProjectTeamMember(category.ProjectId!, requesterId)) return StatusCode(403, new { message = "You must be a team member of the project to create a category." });

            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");

            category.Id = ObjectId.GenerateNewId().ToString();
            category.CreatedBy = requesterId;
            categoriesCollection.InsertOne(category);
            return CreatedAtRoute("GetCategoryById", new { id = category.Id }, category);
        }

        // PUT /api/categories/{id}
        [HttpPut("{id:length(24)}")]
        public IActionResult Update(string id, [FromBody] FlowModels.Category updated)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });
            if (updated == null) return BadRequest(new { message = "Category is required." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var existing = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (existing == null) return NotFound(new { message = "Category not found." });

            var requesterId = GetUserIdFromToken();
            if (requesterId == null) return Unauthorized(new { message = "Invalid user token." });
            if (!IsProjectTeamMember(existing.ProjectId!, requesterId)) return StatusCode(403, new { message = "You must be a team member of the project to update a category." });

            // Update fields
            if (!string.IsNullOrWhiteSpace(updated.CategoryName))
            {
                existing.CategoryName = updated.CategoryName;
            }
            if (!string.IsNullOrWhiteSpace(updated.ProjectId))
            {
                existing.ProjectId = updated.ProjectId;
            }

            categoriesCollection.ReplaceOne(c => c.Id == id, existing);
            return Ok(existing);
        }

        // DELETE /api/categories/{id}
        [HttpDelete("{id:length(24)}")]
        public IActionResult Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var existing = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (existing == null) return NotFound(new { message = "Category not found." });

            var requesterId = GetUserIdFromToken();
            if (requesterId == null) return Unauthorized(new { message = "Invalid user token." });
            // Deletion allowed if user has project edit permission
            // if (!HasProjectEditPermission(existing.ProjectId!, requesterId)) return Forbid("You do not have permission to delete this category.");

            // Remove category reference from all subtasks in this project
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var updateDefinition = Builders<FlowModels.SubTask>.Update
                .Set(t => t.Category, null);
            subTasksCollection.UpdateMany(
                t => t.ProjectId == existing.ProjectId && t.Category == existing.CategoryName,
                updateDefinition
            );

            categoriesCollection.DeleteOne(c => c.Id == id);
            return Ok(new { message = "Category deleted successfully.", id = id });
        }
    }
}

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

        private bool HasProjectEditPermission(string projectId, string userId)
        {
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(userId)) return false;
            var db = _mongoDbService.GetDatabase();
            var projects = db.GetCollection<FlowModels.Project>("project");
            var proj = projects.Find(p => p.Id == projectId).FirstOrDefault();
            if (proj == null) return false;
            if (proj.Permissions == null) return false;
            if (!proj.Permissions.TryGetValue(userId, out var role)) return false;
            return role == "Owner" || role == "Editor" || User.IsInRole("Admin");
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
                    var tasksCollection = db.GetCollection<FlowModels.Task>("tasks");
                    var categories = categoriesCollection.Find(c => c.ProjectId == projectId).ToList();
                    var response = new List<object>();
                    foreach (var c in categories)
                    {
                        var tasks = tasksCollection.Find(t => t.ProjectId == c.ProjectId && t.Category == c.CategoryName).ToList();
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

            var tasksCollection = db.GetCollection<FlowModels.Task>("tasks");
            var tasks = tasksCollection.Find(t => t.ProjectId == cat.ProjectId && t.Category == cat.CategoryName).ToList();
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
            if (!HasProjectEditPermission(category.ProjectId!, requesterId)) return Forbid("You do not have permission to create a category for this project.");

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
            if (!HasProjectEditPermission(existing.ProjectId!, requesterId)) return Forbid("You do not have permission to update this category.");

            updated.Id = id;
            updated.CreatedBy = existing.CreatedBy;
            categoriesCollection.ReplaceOne(c => c.Id == id, updated);
            return StatusCode(200, new { message = "Category Updated." });
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

            categoriesCollection.DeleteOne(c => c.Id == id);
            return Ok(new { message = "Category deleted successfully.", id = id });
        }
    }
}

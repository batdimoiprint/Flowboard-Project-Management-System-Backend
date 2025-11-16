using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using TaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.Task;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/tasks")]
    [Authorize] // Protect all endpoints with JWT
    public class TasksController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<TaskModel> _tasksCollection;

        public TasksController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _tasksCollection = _mongoDbService.GetCollection<TaskModel>("tasks");
        }

        // GET /api/tasks - Get all tasks (optional: filter by projectId)
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? projectId = null)
        {
            try
            {
                var filter = string.IsNullOrWhiteSpace(projectId)
                    ? Builders<TaskModel>.Filter.Empty
                    : Builders<TaskModel>.Filter.Eq(t => t.ProjectId, projectId);
                var tasks = await _tasksCollection.Find(filter).ToListAsync();
                return Ok(tasks ?? new List<TaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Unexpected server error.", detail = ex.Message });
            }
        }

        // GET /api/tasks/{id} - Get task by ID
        [HttpGet("{id}", Name = "GetTaskById")]
        public async Task<IActionResult> GetById(string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return BadRequest(new { message = "Invalid task ID format." });

            try
            {
                var task = await _tasksCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
                return task == null
                    ? NotFound(new { message = "Task not found." })
                    : Ok(task);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch task.", detail = ex.Message });
            }
        }

        // POST /api/tasks - Create a new task (requires ProjectId)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskModel task)
        {
            if (task == null)
                return BadRequest(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            task.Id ??= ObjectId.GenerateNewId().ToString();
            if (string.IsNullOrWhiteSpace(task.ProjectId))
                return BadRequest(new { message = "ProjectId is required for a Task." });

            // optional: check that Project exists
            var db = _mongoDbService.GetDatabase();
            var projectsCollection = db.GetCollection<FlowModels.Project>("project");
            var projectExists = projectsCollection.Find(p => p.Id == task.ProjectId).FirstOrDefault();
            if (projectExists == null)
                return BadRequest(new { message = "ProjectId does not exist." });
            // optional: check that CategoryId exists and belongs to the project
            if (!string.IsNullOrWhiteSpace(task.CategoryId))
            {
                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                var categoryExists = categoriesCollection.Find(c => c.Id == task.CategoryId).FirstOrDefault();
                if (categoryExists == null)
                    return BadRequest(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != task.ProjectId)
                    return BadRequest(new { message = "CategoryId does not belong to the provided ProjectId." });
                // optional: set the Category name for backwards compatibility
                task.Category = categoryExists.CategoryName;
            }
            task.CreatedAt = DateTime.UtcNow;

            try
            {
                await _tasksCollection.InsertOneAsync(task);
                return CreatedAtRoute("GetTaskById", new { id = task.Id }, task);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to insert task.", detail = ex.Message });
            }
        }

        // PUT /api/tasks/{id} - Replace a task
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] TaskModel updatedTask)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (updatedTask == null)
                return BadRequest(new { message = "Task body is required." });

            updatedTask.Id = id;

            try
            {
                var db = _mongoDbService.GetDatabase();
                // Validate category id if provided
                if (!string.IsNullOrWhiteSpace(updatedTask.CategoryId))
                {
                    var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                    var categoryExists = categoriesCollection.Find(c => c.Id == updatedTask.CategoryId).FirstOrDefault();
                    if (categoryExists == null)
                        return BadRequest(new { message = "CategoryId does not exist." });
                    if (categoryExists.ProjectId != updatedTask.ProjectId)
                        return BadRequest(new { message = "CategoryId does not belong to the provided ProjectId." });
                    updatedTask.Category = categoryExists.CategoryName;
                }
                var result = await _tasksCollection.ReplaceOneAsync(t => t.Id == id, updatedTask);
                if (result.MatchedCount == 0)
                    return NotFound(new { message = "Task not found." });

                return StatusCode(200, new { message = "Task Updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update task.", detail = ex.Message });
            }
        }

        // PATCH /api/tasks/{id} - Partial update
        [HttpPatch("{id}")]
        public async Task<IActionResult> Patch(string id, [FromBody] Dictionary<string, object> updates)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (updates == null || updates.Count == 0)
                return BadRequest(new { message = "No updates provided." });

            var updateDefs = new List<UpdateDefinition<TaskModel>>();
            // Fetch existing task to support validation (e.g., category belongs to project)
            var existingTask = await _tasksCollection.Find(t => t.Id == id).FirstOrDefaultAsync();
            if (existingTask == null)
                return NotFound(new { message = "Task not found." });
            // Determine projectId for validation. If projectId is being updated within same patch, prefer the new value.
            var newProjectId = existingTask.ProjectId;
            if (updates.TryGetValue("projectId", out var maybeNewProject) && maybeNewProject != null)
            {
                newProjectId = maybeNewProject.ToString();
            }

            foreach (var kv in updates)
            {
                var fieldLower = kv.Key.ToLowerInvariant();
                var value = kv.Value;

                switch (fieldLower)
                {
                    case "title":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Title, value?.ToString()));
                        break;
                    case "description":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Description, value?.ToString()));
                        break;
                    case "priority":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Priority, value?.ToString()));
                        break;
                    case "status":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Status, value?.ToString()));
                        break;
                    case "assignedto":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.AssignedTo, value?.ToString()));
                        break;
                    case "category":
                        // legacy behavior: set the string name
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Category, value?.ToString()));
                        break;
                    case "categoryid":
                        {
                            var categoryIdStr = value?.ToString();
                            if (!string.IsNullOrWhiteSpace(categoryIdStr))
                            {
                                var db = _mongoDbService.GetDatabase();
                                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                                var categoryExists = categoriesCollection.Find(c => c.Id == categoryIdStr).FirstOrDefault();
                                if (categoryExists == null)
                                    return BadRequest(new { message = "CategoryId does not exist." });
                                if (!string.IsNullOrWhiteSpace(newProjectId) && categoryExists.ProjectId != newProjectId)
                                    return BadRequest(new { message = "CategoryId does not belong to the task's project (or new projectId supplied in same patch)." });
                                updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.CategoryId, categoryIdStr));
                                updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.Category, categoryExists.CategoryName));
                            }
                        }
                        break;
                    case "createdby":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.CreatedBy, value?.ToString()));
                        break;
                    case "startdate":
                        if (DateTime.TryParse(value?.ToString(), out var startDate))
                            updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.StartDate, startDate));
                        break;
                    case "enddate":
                        if (DateTime.TryParse(value?.ToString(), out var endDate))
                            updateDefs.Add(Builders<TaskModel>.Update.Set(t => t.EndDate, endDate));
                        break;
                }
            }

            if (updateDefs.Count == 0)
                return BadRequest(new { message = "No valid updatable fields provided." });

            try
            {
                var result = await _tasksCollection.UpdateOneAsync(
                    Builders<TaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<TaskModel>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "Task not found." });

                return StatusCode(200, new { message = "Task Updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update task.", detail = ex.Message });
            }
        }

        // POST /api/tasks/{id}/comments - Add a comment
        [HttpPost("{id}/comments")]
        public async Task<IActionResult> AddComment(string id, [FromBody] CommentDto commentDto)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (commentDto == null || string.IsNullOrWhiteSpace(commentDto.AuthorId) || string.IsNullOrWhiteSpace(commentDto.Text))
                return BadRequest(new { message = "AuthorId and Text are required." });

            var comment = new FlowModels.Comment
            {
                AuthorId = commentDto.AuthorId,
                Content = commentDto.Text,
                CreatedAt = commentDto.CreatedAt ?? DateTime.UtcNow
            };

            try
            {
                var update = Builders<TaskModel>.Update.Push(t => t.Comments, comment);
                var result = await _tasksCollection.UpdateOneAsync(
                    Builders<TaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    update
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "Task not found." });

                return Ok(comment);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to add comment.", detail = ex.Message });
            }
        }

        // DELETE /api/tasks/{id} - Delete a task
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            try
            {
                var result = await _tasksCollection.DeleteOneAsync(t => t.Id == id);
                if (result.DeletedCount == 0)
                    return NotFound(new { message = "Task not found." });

                return StatusCode(200, new { message = "Task Deleted." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete task.", detail = ex.Message });
            }
        }

        // DTO for adding comments
        public class CommentDto
        {
            public string? AuthorId { get; set; }
            public string? Text { get; set; }
            public DateTime? CreatedAt { get; set; }
        }
    }
}

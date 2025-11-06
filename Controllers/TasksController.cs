using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using TaskModel = Flowboard_Project_Management_System_Backend.Models.Task;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/tasks")]
    public class TasksController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<TaskModel> _tasksCollection;

        public TasksController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _tasksCollection = _mongoDbService.GetCollection<TaskModel>("tasks");
        }

        // --------------------------------------------------------------------
        // ✅ GET /api/tasks - Get all tasks
        // --------------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var tasks = await _tasksCollection.Find(Builders<TaskModel>.Filter.Empty).ToListAsync();
                return Ok(tasks ?? new List<TaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Unexpected server error.", detail = ex.Message });
            }
        }

        // --------------------------------------------------------------------
        // ✅ GET /api/tasks/{id} - Get task by ID
        // --------------------------------------------------------------------
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

        // --------------------------------------------------------------------
        // ✅ POST /api/tasks - Create a new task
        // --------------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaskModel task)
        {
            if (task == null)
                return BadRequest(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            task.Id ??= ObjectId.GenerateNewId().ToString();
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

        // --------------------------------------------------------------------
        // ✅ PUT /api/tasks/{id} - Replace a task
        // --------------------------------------------------------------------
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
                var result = await _tasksCollection.ReplaceOneAsync(t => t.Id == id, updatedTask);
                if (result.MatchedCount == 0)
                    return NotFound(new { message = "Task not found." });

                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update task.", detail = ex.Message });
            }
        }

        // --------------------------------------------------------------------
        // ✅ PATCH /api/tasks/{id} - Partial update
        // --------------------------------------------------------------------
        [HttpPatch("{id}")]
        public async Task<IActionResult> Patch(string id, [FromBody] Dictionary<string, object> updates)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (updates == null || updates.Count == 0)
                return BadRequest(new { message = "No updates provided." });

            var updateDefs = new List<UpdateDefinition<TaskModel>>();

            foreach (var kv in updates)
            {
                var field = kv.Key;
                var value = kv.Value;

                switch (field.ToLowerInvariant())
                {
                    case "title":
                    case "description":
                    case "priority":
                    case "status":
                    case "assignedto":
                    case "categoryid":
                    case "createdby":
                        updateDefs.Add(Builders<TaskModel>.Update.Set(field, value?.ToString()));
                        break;

                    case "startdate":
                    case "enddate":
                        if (DateTime.TryParse(value?.ToString(), out var date))
                            updateDefs.Add(Builders<TaskModel>.Update.Set(field, date));
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

                return NoContent();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update task.", detail = ex.Message });
            }
        }

        // --------------------------------------------------------------------
        // ✅ POST /api/tasks/{id}/comments - Add a comment
        // --------------------------------------------------------------------
        [HttpPost("{id}/comments")]
        public async Task<IActionResult> AddComment(string id, [FromBody] CommentDto commentDto)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (commentDto == null || string.IsNullOrWhiteSpace(commentDto.AuthorId) || string.IsNullOrWhiteSpace(commentDto.Text))
                return BadRequest(new { message = "AuthorId and Text are required." });

            var comment = new Comment
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

        // --------------------------------------------------------------------
        // ✅ DELETE /api/tasks/{id} - Delete a task
        // --------------------------------------------------------------------
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

                return NoContent();
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

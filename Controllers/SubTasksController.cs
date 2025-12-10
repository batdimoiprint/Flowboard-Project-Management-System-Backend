using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using SubTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.SubTask;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/subtasks")]
    [Authorize] // Protect all endpoints with JWT
    public class SubTasksController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<SubTaskModel> _subTasksCollection;

        public SubTasksController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _subTasksCollection = _mongoDbService.GetCollection<SubTaskModel>("subtasks");
        }

        // GET /api/subtasks - Get all subtasks (optional: filter by projectId)
        // Clients can only see tasks from their assigned projects
        [HttpGet]
        [Authorize(Policy = "ProjectRead")]
        public async Task<IActionResult> GetAll([FromQuery] string? projectId = null)
        {
            try
            {
                var filter = string.IsNullOrWhiteSpace(projectId)
                    ? Builders<SubTaskModel>.Filter.Empty
                    : Builders<SubTaskModel>.Filter.Eq(st => st.ProjectId, projectId);

                // If client, verify they have access to the project
                if (User.IsInRole("Client") && !string.IsNullOrWhiteSpace(projectId))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    
                    var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return StatusCode(403, new { message = "You do not have permission to view tasks from this project." });
                }

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return Ok(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Unexpected server error.", detail = ex.Message });
            }
        }

        // GET /api/subtasks/project/{projectId} - Get all subtasks for a specific project
        [HttpGet("project/{projectId}")]
        [Authorize(Policy = "ProjectRead")]
        public async Task<IActionResult> GetByProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return BadRequest(new { message = "ProjectId is required." });

            try
            {
                // If client, verify they have access to the project
                if (User.IsInRole("Client"))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    
                    var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return StatusCode(403, new { message = "You do not have permission to view tasks from this project." });
                }

                var subTasks = await _subTasksCollection.Find(st => st.ProjectId == projectId).ToListAsync();
                return Ok(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch subtasks for project.", detail = ex.Message });
            }
        }

        // GET /api/subtasks/me - Get subtasks for the currently authenticated user
        [HttpGet("me")]
        public async Task<IActionResult> GetForCurrentUser()
        {
            try
            {
                var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                             ?? User?.FindFirst("id")?.Value;

                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized(new { message = "Unable to determine current user from token." });

                var filter = Builders<SubTaskModel>.Filter.Or(
                    Builders<SubTaskModel>.Filter.AnyEq(st => st.AssignedTo, userId),
                    Builders<SubTaskModel>.Filter.Eq(st => st.CreatedBy, userId)
                );

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return Ok(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch current user subtasks.", detail = ex.Message });
            }
        }

        // GET /api/subtasks/user/{userId} - Get subtasks involving a specific user
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetByUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { message = "UserId is required." });

            try
            {
                var filter = Builders<SubTaskModel>.Filter.Or(
                    Builders<SubTaskModel>.Filter.AnyEq(st => st.AssignedTo, userId),
                    Builders<SubTaskModel>.Filter.Eq(st => st.CreatedBy, userId)
                );

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return Ok(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch user subtasks.", detail = ex.Message });
            }
        }

        // GET /api/subtasks/{id} - Get subtask by ID
        // Clients can only view subtasks from their assigned projects
        [HttpGet("{id}", Name = "GetSubTaskById")]
        [Authorize(Policy = "ProjectRead")]
        public async Task<IActionResult> GetById(string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return BadRequest(new { message = "Invalid subtask ID format." });

            try
            {
                var subTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
                if (subTask == null)
                    return NotFound(new { message = "SubTask not found." });

                // If client, verify they have access to this task's project
                if (User.IsInRole("Client"))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    
                    var project = await projectsCollection.Find(p => p.Id == subTask.ProjectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return StatusCode(403, new { message = "You do not have permission to view this task." });
                }

                return Ok(subTask);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch subtask.", detail = ex.Message });
            }
        }

        // POST /api/subtasks - Create a new subtask
        [HttpPost]
        [Authorize(Policy = "DetailedTaskCreate")]
        public async Task<IActionResult> Create([FromBody] CreateSubTaskDto subTaskDto)
        {
            if (subTaskDto == null)
                return BadRequest(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Validate required fields
            if (string.IsNullOrWhiteSpace(subTaskDto.Title))
                return BadRequest(new { message = "Title is required." });

            if (string.IsNullOrWhiteSpace(subTaskDto.ProjectId))
                return BadRequest(new { message = "ProjectId is required." });

            // Parse dates from string format (YYYY-MM-DD)
            DateTime? startDate = null;
            DateTime? endDate = null;

            if (!string.IsNullOrWhiteSpace(subTaskDto.StartDate))
            {
                if (DateTime.TryParse(subTaskDto.StartDate, out var parsedStart))
                    startDate = DateTime.SpecifyKind(parsedStart, DateTimeKind.Utc);
            }

            if (!string.IsNullOrWhiteSpace(subTaskDto.EndDate))
            {
                if (DateTime.TryParse(subTaskDto.EndDate, out var parsedEnd))
                    endDate = DateTime.SpecifyKind(parsedEnd, DateTimeKind.Utc);
            }

            var subTask = new SubTaskModel
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Title = subTaskDto.Title,
                Description = subTaskDto.Description,
                Priority = subTaskDto.Priority,
                MainTaskId = subTaskDto.MainTaskId,
                ProjectId = subTaskDto.ProjectId,
                CategoryId = subTaskDto.CategoryId,
                Category = subTaskDto.Category,
                CreatedBy = subTaskDto.CreatedBy,
                AssignedTo = subTaskDto.AssignedTo ?? new List<string>(),
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = DateTime.UtcNow
            };

            // Validate categoryId if provided
            if (!string.IsNullOrWhiteSpace(subTask.CategoryId))
            {
                var db = _mongoDbService.GetDatabase();
                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                var categoryExists = categoriesCollection.Find(c => c.Id == subTask.CategoryId).FirstOrDefault();
                if (categoryExists == null)
                    return BadRequest(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != subTask.ProjectId)
                    return BadRequest(new { message = "CategoryId does not belong to the provided ProjectId." });
                subTask.Category = categoryExists.CategoryName;
            }

            try
            {
                await _subTasksCollection.InsertOneAsync(subTask);

                // If MainTaskId is provided, add this SubTaskId to the MainTask's SubTaskIds array
                if (!string.IsNullOrWhiteSpace(subTask.MainTaskId))
                {
                    var db = _mongoDbService.GetDatabase();
                    var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("mainTasks");
                    var mainTaskFilter = Builders<FlowModels.MainTask>.Filter.Eq(m => m.Id, subTask.MainTaskId);
                    var updateDef = Builders<FlowModels.MainTask>.Update.AddToSet(m => m.SubTaskIds, subTask.Id);
                    await mainTasksCollection.UpdateOneAsync(mainTaskFilter, updateDef);
                }

                return CreatedAtRoute("GetSubTaskById", new { id = subTask.Id }, subTask);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to insert subtask.", detail = ex.Message });
            }
        }

        // PUT /api/subtasks/{id} - Replace a subtask
        [HttpPut("{id}")]
        [Authorize(Policy = "DetailedTaskUpdate")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateSubTaskDto subTaskDto)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (subTaskDto == null)
                return BadRequest(new { message = "SubTask body is required." });

            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return NotFound(new { message = "SubTask not found." });

            // Parse dates from string format (YYYY-MM-DD)
            DateTime? startDate = null;
            DateTime? endDate = null;

            if (!string.IsNullOrWhiteSpace(subTaskDto.StartDate))
            {
                if (DateTime.TryParse(subTaskDto.StartDate, out var parsedStart))
                    startDate = DateTime.SpecifyKind(parsedStart, DateTimeKind.Utc);
            }

            if (!string.IsNullOrWhiteSpace(subTaskDto.EndDate))
            {
                if (DateTime.TryParse(subTaskDto.EndDate, out var parsedEnd))
                    endDate = DateTime.SpecifyKind(parsedEnd, DateTimeKind.Utc);
            }

            var updatedSubTask = new SubTaskModel
            {
                Id = id,
                Title = subTaskDto.Title,
                Description = subTaskDto.Description,
                Priority = subTaskDto.Priority,
                MainTaskId = subTaskDto.MainTaskId,
                ProjectId = subTaskDto.ProjectId,
                CategoryId = subTaskDto.CategoryId,
                Category = subTaskDto.Category,
                CreatedBy = subTaskDto.CreatedBy,
                AssignedTo = subTaskDto.AssignedTo ?? new List<string>(),
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = existingSubTask.CreatedAt,
                Comments = existingSubTask.Comments
            };

            // Validate categoryId if provided
            if (!string.IsNullOrWhiteSpace(updatedSubTask.CategoryId))
            {
                var db = _mongoDbService.GetDatabase();
                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                var categoryExists = categoriesCollection.Find(c => c.Id == updatedSubTask.CategoryId).FirstOrDefault();
                if (categoryExists == null)
                    return BadRequest(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != updatedSubTask.ProjectId)
                    return BadRequest(new { message = "CategoryId does not belong to the provided ProjectId." });
                updatedSubTask.Category = categoryExists.CategoryName;
            }

            try
            {
                // Handle MainTaskId changes
                if (existingSubTask.MainTaskId != updatedSubTask.MainTaskId)
                {
                    var db = _mongoDbService.GetDatabase();
                    var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("mainTasks");

                    // Remove from old MainTask if it exists
                    if (!string.IsNullOrWhiteSpace(existingSubTask.MainTaskId))
                    {
                        var oldMainTaskFilter = Builders<FlowModels.MainTask>.Filter.Eq(m => m.Id, existingSubTask.MainTaskId);
                        var removeDef = Builders<FlowModels.MainTask>.Update.Pull(m => m.SubTaskIds, id);
                        await mainTasksCollection.UpdateOneAsync(oldMainTaskFilter, removeDef);
                    }

                    // Add to new MainTask if provided
                    if (!string.IsNullOrWhiteSpace(updatedSubTask.MainTaskId))
                    {
                        var newMainTaskFilter = Builders<FlowModels.MainTask>.Filter.Eq(m => m.Id, updatedSubTask.MainTaskId);
                        var addDef = Builders<FlowModels.MainTask>.Update.AddToSet(m => m.SubTaskIds, id);
                        await mainTasksCollection.UpdateOneAsync(newMainTaskFilter, addDef);
                    }
                }

                var result = await _subTasksCollection.ReplaceOneAsync(st => st.Id == id, updatedSubTask);
                if (result.MatchedCount == 0)
                    return NotFound(new { message = "SubTask not found." });

                return StatusCode(200, new { message = "SubTask Updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update subtask.", detail = ex.Message });
            }
        }

        // PATCH /api/subtasks/{id} - Partial update
        [HttpPatch("{id}")]
        [Authorize(Policy = "DetailedTaskEdit")]
        public async Task<IActionResult> Patch(string id, [FromBody] Dictionary<string, object> updates)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (updates == null || updates.Count == 0)
                return BadRequest(new { message = "No updates provided." });

            var updateDefs = new List<UpdateDefinition<SubTaskModel>>();
            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return NotFound(new { message = "SubTask not found." });

            foreach (var kv in updates)
            {
                var fieldLower = kv.Key.ToLowerInvariant();
                var value = kv.Value;

                switch (fieldLower)
                {
                    case "title":
                        updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Title, value?.ToString()));
                        break;
                    case "description":
                        updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Description, value?.ToString()));
                        break;
                    case "priority":
                        updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Priority, value?.ToString()));
                        break;
                    case "status":
                        updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Status, value?.ToString()));
                        break;
                    case "startdate":
                        {
                            var dateStr = value?.ToString();
                            if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                            {
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.StartDate, parsedDate));
                            }
                            else if (string.IsNullOrWhiteSpace(dateStr))
                            {
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.StartDate, null));
                            }
                            break;
                        }
                    case "enddate":
                        {
                            var dateStr = value?.ToString();
                            if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var parsedDate))
                            {
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.EndDate, parsedDate));
                            }
                            else if (string.IsNullOrWhiteSpace(dateStr))
                            {
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.EndDate, null));
                            }
                            break;
                        }
                    case "categoryid":
                        {
                            var categoryIdValue = value?.ToString();
                            // Treat "uncategorized" (or empty) as clearing the category
                            if (string.IsNullOrWhiteSpace(categoryIdValue) || categoryIdValue.Equals("uncategorized", StringComparison.OrdinalIgnoreCase))
                            {
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.CategoryId, null));
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Category, null));
                            }
                            else
                            {
                                if (!ObjectId.TryParse(categoryIdValue, out _))
                                    return BadRequest(new { message = "CategoryId must be a valid ObjectId or 'uncategorized'." });
                                updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.CategoryId, categoryIdValue));
                            }
                            break;
                        }
                    case "assignedto":
                        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                        {
                            var list = JsonSerializer.Deserialize<List<string>>(element.GetRawText());
                            updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.AssignedTo, list ?? new List<string>()));
                        }
                        else
                        {
                            var str = value?.ToString();
                            var list = string.IsNullOrEmpty(str) ? new List<string>() : new List<string> { str };
                            updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.AssignedTo, list));
                        }
                        break;
                }
            }

            if (updateDefs.Count == 0)
                return BadRequest(new { message = "No valid updatable fields provided." });

            try
            {
                var result = await _subTasksCollection.UpdateOneAsync(
                    Builders<SubTaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<SubTaskModel>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "SubTask not found." });

                return StatusCode(200, new { message = "SubTask Updated." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update subtask.", detail = ex.Message });
            }
        }

        // PATCH /api/subtasks/{id}/category - Update category of a subtask
        [HttpPatch("{id}/category")]
        [Authorize(Policy = "DetailedTaskEdit")]
        public async Task<IActionResult> UpdateCategory(string id, [FromBody] UpdateCategoryDto categoryDto)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            if (categoryDto == null)
                return BadRequest(new { message = "Category data is required." });

            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return NotFound(new { message = "SubTask not found." });

            // Validate categoryId if provided and not empty
            if (!string.IsNullOrWhiteSpace(categoryDto.CategoryId))
            {
                var db = _mongoDbService.GetDatabase();
                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                var categoryExists = categoriesCollection.Find(c => c.Id == categoryDto.CategoryId).FirstOrDefault();
                if (categoryExists == null)
                    return BadRequest(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != existingSubTask.ProjectId)
                    return BadRequest(new { message = "CategoryId does not belong to the same project." });
            }

            try
            {
                var updateDefs = new List<UpdateDefinition<SubTaskModel>>();

                if (!string.IsNullOrWhiteSpace(categoryDto.CategoryId))
                {
                    updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.CategoryId, categoryDto.CategoryId));
                    var db = _mongoDbService.GetDatabase();
                    var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                    var categoryExists = categoriesCollection.Find(c => c.Id == categoryDto.CategoryId).FirstOrDefault();
                    if (categoryExists != null)
                    {
                        updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Category, categoryExists.CategoryName));
                    }
                }
                else if (string.IsNullOrWhiteSpace(categoryDto.CategoryId) && categoryDto.CategoryId == "")
                {
                    // Clear category
                    updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.CategoryId, null));
                    updateDefs.Add(Builders<SubTaskModel>.Update.Set(st => st.Category, null));
                }

                if (updateDefs.Count == 0)
                    return BadRequest(new { message = "No valid category updates provided." });

                var update = Builders<SubTaskModel>.Update.Combine(updateDefs);
                var result = await _subTasksCollection.UpdateOneAsync(
                    Builders<SubTaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    update
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "SubTask not found." });

                return StatusCode(200, new { message = "Category updated successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update category.", detail = ex.Message });
            }
        }

        // POST /api/subtasks/{id}/comments - Add a comment
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
                var update = Builders<SubTaskModel>.Update.Push(st => st.Comments, comment);
                var result = await _subTasksCollection.UpdateOneAsync(
                    Builders<SubTaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    update
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "SubTask not found." });

                return Ok(comment);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to add comment.", detail = ex.Message });
            }
        }

        // DELETE /api/subtasks/{id} - Delete a subtask
        [HttpDelete("{id}")]
        [Authorize(Policy = "DetailedTaskDelete")]
        public async Task<IActionResult> Delete(string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            try
            {
                // Get the SubTask first to retrieve its MainTaskId
                var subTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
                if (subTask == null)
                    return NotFound(new { message = "SubTask not found." });

                var result = await _subTasksCollection.DeleteOneAsync(st => st.Id == id);
                if (result.DeletedCount == 0)
                    return NotFound(new { message = "SubTask not found." });

                // If MainTaskId exists, remove this SubTaskId from the MainTask's SubTaskIds array
                if (!string.IsNullOrWhiteSpace(subTask.MainTaskId))
                {
                    var db = _mongoDbService.GetDatabase();
                    var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("mainTasks");
                    var mainTaskFilter = Builders<FlowModels.MainTask>.Filter.Eq(m => m.Id, subTask.MainTaskId);
                    var updateDef = Builders<FlowModels.MainTask>.Update.Pull(m => m.SubTaskIds, id);
                    await mainTasksCollection.UpdateOneAsync(mainTaskFilter, updateDef);
                }

                return StatusCode(200, new { message = "SubTask Deleted." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete subtask.", detail = ex.Message });
            }
        }

        // DTO for updating category
        public class UpdateCategoryDto
        {
            public string? CategoryId { get; set; }
            public string? Category { get; set; }
        }

        // DTO for adding comments
        public class CommentDto
        {
            public string? AuthorId { get; set; }
            public string? Text { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        // DTO for creating subtasks
        public class CreateSubTaskDto
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? Priority { get; set; }
            public string? MainTaskId { get; set; }
            public string? ProjectId { get; set; }
            public string? Category { get; set; }
            public string? CategoryId { get; set; }
            public string? CreatedBy { get; set; }
            public List<string>? AssignedTo { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
        }

        // DTO for updating subtasks
        public class UpdateSubTaskDto
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? Priority { get; set; }
            public string? MainTaskId { get; set; }
            public string? ProjectId { get; set; }
            public string? Category { get; set; }
            public string? CategoryId { get; set; }
            public string? CreatedBy { get; set; }
            public List<string>? AssignedTo { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
        }
    }
}

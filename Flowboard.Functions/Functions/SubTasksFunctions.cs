using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using SubTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.SubTask;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/SubTasksController.cs (see HomeFunctions.cs for the mapping rules
    /// this follows).
    ///
    /// Original: [Route("api/subtasks")], class-level [Authorize], plus several method-level
    /// [Authorize(Policy = "ProjectRead"/"DetailedTaskCreate"/"DetailedTaskUpdate"/"DetailedTaskEdit"/"DetailedTaskDelete")].
    /// Per JwtAuthMiddleware's doc comment, all of those policies are nothing but
    /// RequireAuthenticatedUser() (see Configurations/SecurityConfiguration.cs) - i.e. different
    /// names for one identical rule with no distinct behavior. Default-deny in
    /// JwtAuthMiddleware already enforces exactly that for every function below (none carry
    /// [AllowAnonymous] - the controller has zero [AllowAnonymous] endpoints), so no policy
    /// engine is needed - the simplification is behavior preserving.
    ///
    /// Role/ownership checks (User.IsInRole("Client"), NameIdentifier claim lookups) are ported
    /// faithfully against req.HttpContext.User, which JwtAuthMiddleware populates from the
    /// validated token (see JwtAuthMiddleware.Invoke -> httpContext.User = principal).
    /// </summary>
    public class SubTasksFunctions
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<SubTaskModel> _subTasksCollection;

        public SubTasksFunctions(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _subTasksCollection = _mongoDbService.GetCollection<SubTaskModel>("subtasks");
        }

        // GET /api/subtasks - Get all subtasks (optional: filter by projectId)
        // Clients can only see tasks from their assigned projects
        [Function("SubTasks_GetAll")]
        public async Task<IActionResult> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/subtasks")] HttpRequest req)
        {
            try
            {
                string? projectId = req.Query["projectId"];

                var filter = string.IsNullOrWhiteSpace(projectId)
                    ? Builders<SubTaskModel>.Filter.Empty
                    : Builders<SubTaskModel>.Filter.Eq(st => st.ProjectId, projectId);

                var user = req.HttpContext.User;

                // If client, verify they have access to the project
                if (user.IsInRole("Client") && !string.IsNullOrWhiteSpace(projectId))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return new ObjectResult(new { message = "You do not have permission to view tasks from this project." }) { StatusCode = 403 };
                }

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return new OkObjectResult(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Unexpected server error.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // GET /api/subtasks/project/{projectId} - Get all subtasks for a specific project
        [Function("SubTasks_GetByProject")]
        public async Task<IActionResult> GetByProject(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/subtasks/project/{projectId}")] HttpRequest req,
            string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return new BadRequestObjectResult(new { message = "ProjectId is required." });

            try
            {
                var user = req.HttpContext.User;

                // If client, verify they have access to the project
                if (user.IsInRole("Client"))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return new ObjectResult(new { message = "You do not have permission to view tasks from this project." }) { StatusCode = 403 };
                }

                var subTasks = await _subTasksCollection.Find(st => st.ProjectId == projectId).ToListAsync();
                return new OkObjectResult(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch subtasks for project.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // GET /api/subtasks/me - Get subtasks for the currently authenticated user
        [Function("SubTasks_GetForCurrentUser")]
        public async Task<IActionResult> GetForCurrentUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/subtasks/me")] HttpRequest req)
        {
            try
            {
                var user = req.HttpContext.User;
                var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                             ?? user?.FindFirst("id")?.Value;

                if (string.IsNullOrWhiteSpace(userId))
                    return new UnauthorizedObjectResult(new { message = "Unable to determine current user from token." });

                var filter = Builders<SubTaskModel>.Filter.Or(
                    Builders<SubTaskModel>.Filter.AnyEq(st => st.AssignedTo, userId),
                    Builders<SubTaskModel>.Filter.Eq(st => st.CreatedBy, userId)
                );

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return new OkObjectResult(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch current user subtasks.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // GET /api/subtasks/user/{userId} - Get subtasks involving a specific user
        [Function("SubTasks_GetByUser")]
        public async Task<IActionResult> GetByUser(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/subtasks/user/{userId}")] HttpRequest req,
            string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new BadRequestObjectResult(new { message = "UserId is required." });

            try
            {
                var filter = Builders<SubTaskModel>.Filter.Or(
                    Builders<SubTaskModel>.Filter.AnyEq(st => st.AssignedTo, userId),
                    Builders<SubTaskModel>.Filter.Eq(st => st.CreatedBy, userId)
                );

                var subTasks = await _subTasksCollection.Find(filter).ToListAsync();
                return new OkObjectResult(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch user subtasks.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // GET /api/subtasks/{id} - Get subtask by ID
        // Clients can only view subtasks from their assigned projects
        [Function("SubTasks_GetById")]
        public async Task<IActionResult> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/subtasks/{id:length(24)}")] HttpRequest req,
            string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return new BadRequestObjectResult(new { message = "Invalid subtask ID format." });

            try
            {
                var subTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
                if (subTask == null)
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                var user = req.HttpContext.User;

                // If client, verify they have access to this task's project
                if (user.IsInRole("Client"))
                {
                    var db = _mongoDbService.GetDatabase();
                    var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    var project = await projectsCollection.Find(p => p.Id == subTask.ProjectId).FirstOrDefaultAsync();
                    if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                        return new ObjectResult(new { message = "You do not have permission to view this task." }) { StatusCode = 403 };
                }

                return new OkObjectResult(subTask);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch subtask.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // POST /api/subtasks - Create a new subtask
        [Function("SubTasks_Create")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/subtasks")] HttpRequest req)
        {
            var subTaskDto = await ReadBodyAsync<CreateSubTaskDto>(req);

            if (subTaskDto == null)
                return new BadRequestObjectResult(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            // Validate required fields
            if (string.IsNullOrWhiteSpace(subTaskDto.Title))
                return new BadRequestObjectResult(new { message = "Title is required." });

            if (string.IsNullOrWhiteSpace(subTaskDto.ProjectId))
                return new BadRequestObjectResult(new { message = "ProjectId is required." });

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
                    return new BadRequestObjectResult(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != subTask.ProjectId)
                    return new BadRequestObjectResult(new { message = "CategoryId does not belong to the provided ProjectId." });
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

                // Original used CreatedAtRoute("GetSubTaskById", ...). There is no route-name
                // registry in the isolated worker, so this reproduces the same 201 + body
                // semantics directly against the known route template (api/subtasks/{id}).
                return new ObjectResult(subTask) { StatusCode = 201 };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to insert subtask.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // PUT /api/subtasks/{id} - Replace a subtask
        [Function("SubTasks_Update")]
        public async Task<IActionResult> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/subtasks/{id:length(24)}")] HttpRequest req,
            string id)
        {
            var subTaskDto = await ReadBodyAsync<UpdateSubTaskDto>(req);

            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            if (subTaskDto == null)
                return new BadRequestObjectResult(new { message = "SubTask body is required." });

            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return new NotFoundObjectResult(new { message = "SubTask not found." });

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
                    return new BadRequestObjectResult(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != updatedSubTask.ProjectId)
                    return new BadRequestObjectResult(new { message = "CategoryId does not belong to the provided ProjectId." });
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
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                return new ObjectResult(new { message = "SubTask Updated." }) { StatusCode = 200 };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to update subtask.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // PATCH /api/subtasks/{id} - Partial update
        [Function("SubTasks_Patch")]
        public async Task<IActionResult> Patch(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/subtasks/{id:length(24)}")] HttpRequest req,
            string id)
        {
            var updates = await ReadBodyAsync<Dictionary<string, object>>(req);

            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            if (updates == null || updates.Count == 0)
                return new BadRequestObjectResult(new { message = "No updates provided." });

            var updateDefs = new List<UpdateDefinition<SubTaskModel>>();
            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return new NotFoundObjectResult(new { message = "SubTask not found." });

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
                                    return new BadRequestObjectResult(new { message = "CategoryId must be a valid ObjectId or 'uncategorized'." });
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
                return new BadRequestObjectResult(new { message = "No valid updatable fields provided." });

            try
            {
                var result = await _subTasksCollection.UpdateOneAsync(
                    Builders<SubTaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<SubTaskModel>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                return new ObjectResult(new { message = "SubTask Updated." }) { StatusCode = 200 };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to update subtask.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // PATCH /api/subtasks/{id}/category - Update category of a subtask
        [Function("SubTasks_UpdateCategory")]
        public async Task<IActionResult> UpdateCategory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/subtasks/{id:length(24)}/category")] HttpRequest req,
            string id)
        {
            var categoryDto = await ReadBodyAsync<UpdateCategoryDto>(req);

            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            if (categoryDto == null)
                return new BadRequestObjectResult(new { message = "Category data is required." });

            var existingSubTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
            if (existingSubTask == null)
                return new NotFoundObjectResult(new { message = "SubTask not found." });

            // Validate categoryId if provided and not empty
            if (!string.IsNullOrWhiteSpace(categoryDto.CategoryId))
            {
                var db = _mongoDbService.GetDatabase();
                var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
                var categoryExists = categoriesCollection.Find(c => c.Id == categoryDto.CategoryId).FirstOrDefault();
                if (categoryExists == null)
                    return new BadRequestObjectResult(new { message = "CategoryId does not exist." });
                if (categoryExists.ProjectId != existingSubTask.ProjectId)
                    return new BadRequestObjectResult(new { message = "CategoryId does not belong to the same project." });
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
                    return new BadRequestObjectResult(new { message = "No valid category updates provided." });

                var update = Builders<SubTaskModel>.Update.Combine(updateDefs);
                var result = await _subTasksCollection.UpdateOneAsync(
                    Builders<SubTaskModel>.Filter.Eq("_id", ObjectId.Parse(id)),
                    update
                );

                if (result.MatchedCount == 0)
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                return new ObjectResult(new { message = "Category updated successfully." }) { StatusCode = 200 };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to update category.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // POST /api/subtasks/{id}/comments - Add a comment
        [Function("SubTasks_AddComment")]
        public async Task<IActionResult> AddComment(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/subtasks/{id:length(24)}/comments")] HttpRequest req,
            string id)
        {
            var commentDto = await ReadBodyAsync<CommentDto>(req);

            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            if (commentDto == null || string.IsNullOrWhiteSpace(commentDto.AuthorId) || string.IsNullOrWhiteSpace(commentDto.Text))
                return new BadRequestObjectResult(new { message = "AuthorId and Text are required." });

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
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                return new OkObjectResult(comment);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to add comment.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // DELETE /api/subtasks/{id} - Delete a subtask
        [Function("SubTasks_Delete")]
        public async Task<IActionResult> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/subtasks/{id:length(24)}")] HttpRequest req,
            string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            try
            {
                // Get the SubTask first to retrieve its MainTaskId
                var subTask = await _subTasksCollection.Find(st => st.Id == id).FirstOrDefaultAsync();
                if (subTask == null)
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                var result = await _subTasksCollection.DeleteOneAsync(st => st.Id == id);
                if (result.DeletedCount == 0)
                    return new NotFoundObjectResult(new { message = "SubTask not found." });

                // If MainTaskId exists, remove this SubTaskId from the MainTask's SubTaskIds array
                if (!string.IsNullOrWhiteSpace(subTask.MainTaskId))
                {
                    var db = _mongoDbService.GetDatabase();
                    var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("mainTasks");
                    var mainTaskFilter = Builders<FlowModels.MainTask>.Filter.Eq(m => m.Id, subTask.MainTaskId);
                    var updateDef = Builders<FlowModels.MainTask>.Update.Pull(m => m.SubTaskIds, id);
                    await mainTasksCollection.UpdateOneAsync(mainTaskFilter, updateDef);
                }

                return new ObjectResult(new { message = "SubTask Deleted." }) { StatusCode = 200 };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to delete subtask.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        private static async Task<T?> ReadBodyAsync<T>(HttpRequest req) where T : class
        {
            try
            {
                return await req.ReadFromJsonAsync<T>();
            }
            catch
            {
                return null;
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

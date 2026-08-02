using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using Flowboard_Project_Management_System_Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MongoDB.Bson;
using MongoDB.Driver;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/CategoryController.cs. Original routing:
    ///   [Route("api/categories")]
    ///   [Authorize] (class-level - protects every action)
    ///   [HttpGet]                                   GetAll()              -> GET    api/categories
    ///   [HttpGet("{id:length(24)}", Name="...")]     GetById()             -> GET    api/categories/{id}
    ///   [HttpGet("{id:length(24)}/tasks")]           GetTasksForCategory() -> GET    api/categories/{id}/tasks
    ///   [HttpPost]                                   Create()              -> POST   api/categories
    ///   [HttpPut("{id:length(24)}")]                 Update()              -> PUT    api/categories/{id}
    ///   [HttpDelete("{id:length(24)}")]               Delete()              -> DELETE api/categories/{id}
    ///
    /// The original's {id:length(24)} route constraint (MongoDB ObjectId string length) IS
    /// reproducible - the Functions HTTP trigger uses the same ASP.NET Core route-template
    /// constraint syntax, so Route = "api/categories/{id:length(24)}" is preserved verbatim
    /// below. This matters beyond cosmetics: Category.Id/ProjectId/CreatedBy are declared
    /// [BsonRepresentation(BsonType.ObjectId)], so a non-24-char id reaching the handler and
    /// hitting Mongo throws a FormatException (500) instead of the router 404ing it first, as
    /// the original did.
    ///
    /// CreatedAtRoute("GetCategoryById", new { id = category.Id }, category) resolves to
    /// "api/categories/{id}" (this controller's own route prefix), so it's reproduced here as
    /// an equivalent CreatedResult with that exact Location path and the same body - not an
    /// invented URL, not a bare 201.
    ///
    /// Authenticated user access: JwtAuthMiddleware assigns the validated ClaimsPrincipal
    /// directly to httpContext.User, so req.HttpContext.User is equivalent to the original
    /// controller's ControllerBase.User.
    /// </summary>
    public class CategoryFunctions
    {
        private readonly MongoDbService _mongoDbService;

        public CategoryFunctions(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        private static string? GetUserIdFromToken(HttpRequest req)
        {
            var user = req.HttpContext.User;
            if (user == null) return null;
            var userId =
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                user.FindFirst("id")?.Value ??
                user.FindFirst("userId")?.Value;
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
        [Function("Category_GetAll")]
        public IActionResult GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/categories")] HttpRequest req)
        {
            string? projectId = req.Query.TryGetValue("projectId", out var projectIdValues)
                ? projectIdValues.ToString()
                : null;
            bool includeTasks = req.Query.TryGetValue("includeTasks", out var includeTasksValues)
                && bool.TryParse(includeTasksValues.ToString(), out var includeTasksParsed)
                && includeTasksParsed;

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
                    return new OkObjectResult(response);
                }
                else
                {
                    var cats = categoriesCollection.Find(c => c.ProjectId == projectId).ToList();
                    return new OkObjectResult(cats);
                }
            }

            var all = categoriesCollection.Find(_ => true).ToList();
            return new OkObjectResult(all);
        }

        // GET /api/categories/{id}
        [Function("Category_GetById")]
        public IActionResult GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/categories/{id:length(24)}")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return new BadRequestObjectResult(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var cat = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (cat == null) return new NotFoundObjectResult(new { message = "Category not found." });
            return new OkObjectResult(cat);
        }

        // GET /api/categories/{id}/tasks
        [Function("Category_GetTasksForCategory")]
        public IActionResult GetTasksForCategory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/categories/{id:length(24)}/tasks")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return new BadRequestObjectResult(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var cat = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (cat == null) return new NotFoundObjectResult(new { message = "Category not found." });

            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var tasks = subTasksCollection.Find(t => t.ProjectId == cat.ProjectId && t.Category == cat.CategoryName).ToList();
            return new OkObjectResult(tasks);
        }

        // POST /api/categories - create new category
        [Function("Category_Create")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/categories")] HttpRequest req)
        {
            FlowModels.Category? category;
            try
            {
                category = await req.ReadFromJsonAsync<FlowModels.Category>();
            }
            catch
            {
                category = null;
            }

            if (category == null) return new BadRequestObjectResult(new { message = "Category is required." });
            if (string.IsNullOrWhiteSpace(category.ProjectId)) return new BadRequestObjectResult(new { message = "ProjectId is required." });
            if (string.IsNullOrWhiteSpace(category.CategoryName)) return new BadRequestObjectResult(new { message = "CategoryName is required." });

            var requesterId = GetUserIdFromToken(req);
            if (requesterId == null) return new UnauthorizedObjectResult(new { message = "Invalid user token." });
            if (!IsProjectTeamMember(category.ProjectId!, requesterId))
                return new ObjectResult(new { message = "You must be a team member of the project to create a category." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };

            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");

            category.Id = ObjectId.GenerateNewId().ToString();
            category.CreatedBy = requesterId;
            categoriesCollection.InsertOne(category);

            return new CreatedResult($"/api/categories/{category.Id}", category);
        }

        // PUT /api/categories/{id}
        [Function("Category_Update")]
        public async Task<IActionResult> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/categories/{id:length(24)}")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return new BadRequestObjectResult(new { message = "Invalid id." });

            FlowModels.Category? updated;
            try
            {
                updated = await req.ReadFromJsonAsync<FlowModels.Category>();
            }
            catch
            {
                updated = null;
            }

            if (updated == null) return new BadRequestObjectResult(new { message = "Category is required." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var existing = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (existing == null) return new NotFoundObjectResult(new { message = "Category not found." });

            var requesterId = GetUserIdFromToken(req);
            if (requesterId == null) return new UnauthorizedObjectResult(new { message = "Invalid user token." });
            if (!IsProjectTeamMember(existing.ProjectId!, requesterId))
                return new ObjectResult(new { message = "You must be a team member of the project to update a category." })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };

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
            return new OkObjectResult(existing);
        }

        // DELETE /api/categories/{id}
        [Function("Category_Delete")]
        public IActionResult Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/categories/{id:length(24)}")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return new BadRequestObjectResult(new { message = "Invalid id." });
            var db = _mongoDbService.GetDatabase();
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var existing = categoriesCollection.Find(c => c.Id == id).FirstOrDefault();
            if (existing == null) return new NotFoundObjectResult(new { message = "Category not found." });

            var requesterId = GetUserIdFromToken(req);
            if (requesterId == null) return new UnauthorizedObjectResult(new { message = "Invalid user token." });
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
            return new OkObjectResult(new { message = "Category deleted successfully.", id = id });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Flowboard.Functions.Middleware;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MainTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.MainTask;
using SubTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.SubTask;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/MainTasksController.cs (see HomeFunctions.cs for the mapping rules
    /// this follows).
    ///
    /// Original: [Route("api/maintasks")], class-level [Authorize], plus three method-level
    /// [Authorize(Policy = "DetailedTaskEdit"/"DetailedTaskCreate"/"DetailedTaskDelete")].
    /// Per JwtAuthMiddleware's doc comment, all three of those policies are nothing but
    /// RequireAuthenticatedUser() (see Configurations/SecurityConfiguration.cs) - i.e. three
    /// names for one identical rule with no distinct behavior. Default-deny in
    /// JwtAuthMiddleware already enforces exactly that for every function below (none carry
    /// [AllowAnonymous]), so no policy engine is needed - the simplification is behavior
    /// preserving.
    /// </summary>
    public class MainTasksFunctions
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<MainTaskModel> _mainTasksCollection;
        private readonly IMongoCollection<SubTaskModel> _subTasksCollection;

        public MainTasksFunctions(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _mainTasksCollection = _mongoDbService.GetCollection<MainTaskModel>("maintasks");
            _subTasksCollection = _mongoDbService.GetCollection<SubTaskModel>("subtasks");
        }

        // PUT /api/maintasks/{id} - Update a main task
        [Function("MainTasks_Update")]
        public async Task<IActionResult> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/maintasks/{id}")] HttpRequest req,
            string id)
        {
            var dto = await ReadBodyAsync<UpdateMainTaskDto>(req);

            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid main task ID format." });
            if (dto == null)
                return new BadRequestObjectResult(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });
            if (string.IsNullOrWhiteSpace(dto.Title))
                return new BadRequestObjectResult(new { message = "Title is required." });

            var update = Builders<MainTaskModel>.Update
                .Set(x => x.Title, dto.Title)
                .Set(x => x.Description, dto.Description)
                .Set(x => x.StartDate, dto.StartDate)
                .Set(x => x.EndDate, dto.EndDate);

            var result = await _mainTasksCollection.UpdateOneAsync(x => x.Id == id, update);
            if (result.MatchedCount == 0)
                return new NotFoundObjectResult(new { message = "MainTask not found." });
            return new NoContentResult();
        }

        // GET /api/maintasks - Get all main tasks
        [Function("MainTasks_GetAll")]
        public async Task<IActionResult> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/maintasks")] HttpRequest req)
        {
            try
            {
                var mainTasks = await _mainTasksCollection.Find(_ => true).ToListAsync();
                return new OkObjectResult(mainTasks ?? new List<MainTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Unexpected server error.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        // GET /api/maintasks/project/{projectId} - Get main tasks by project
        [Function("MainTasks_GetByProject")]
        public async Task<IActionResult> GetByProject(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/maintasks/project/{projectId}")] HttpRequest req,
            string projectId)
        {
            if (!ObjectId.TryParse(projectId, out _))
                return new BadRequestObjectResult(new { message = "Invalid project ID format." });

            try
            {
                var mainTasks = await _mainTasksCollection.Find(mt => mt.ProjectId == projectId).ToListAsync();
                return new OkObjectResult(mainTasks ?? new List<MainTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch main tasks for project.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        // GET /api/maintasks/{id} - Get main task by ID
        [Function("MainTasks_GetById")]
        public async Task<IActionResult> GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/maintasks/{id}")] HttpRequest req,
            string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return new BadRequestObjectResult(new { message = "Invalid main task ID format." });

            try
            {
                var mainTask = await _mainTasksCollection.Find(mt => mt.Id == id).FirstOrDefaultAsync();
                return mainTask == null
                    ? new NotFoundObjectResult(new { message = "MainTask not found." })
                    : new OkObjectResult(mainTask);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch main task.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        // POST /api/maintasks - Create a new main task
        [Function("MainTasks_Create")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/maintasks")] HttpRequest req)
        {
            var mainTaskDto = await ReadBodyAsync<CreateMainTaskDto>(req);

            if (mainTaskDto == null)
                return new BadRequestObjectResult(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            if (string.IsNullOrWhiteSpace(mainTaskDto.Title))
                return new BadRequestObjectResult(new { message = "Title is required." });

            try
            {
                var mainTask = new MainTaskModel
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Title = mainTaskDto.Title,
                    Description = mainTaskDto.Description,
                    StartDate = mainTaskDto.StartDate,
                    EndDate = mainTaskDto.EndDate,
                    ProjectId = mainTaskDto.ProjectId,
                    CreatedAt = DateTime.UtcNow
                };

                await _mainTasksCollection.InsertOneAsync(mainTask);

                // Original used CreatedAtRoute("GetMainTaskById", ...). There is no route-name
                // registry in the isolated worker, so this reproduces the same 201 + Location
                // header + body semantics directly against the known route template.
                return new CreatedResult($"/api/maintasks/{mainTask.Id}", mainTask);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to create main task.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        // GET /api/maintasks/{id}/subtasks - Get all subtasks for a main task
        [Function("MainTasks_GetSubTasks")]
        public async Task<IActionResult> GetSubTasks(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/maintasks/{id}/subtasks")] HttpRequest req,
            string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid main task ID format." });

            try
            {
                var mainTaskExists = await _mainTasksCollection.Find(mt => mt.Id == id).FirstOrDefaultAsync();
                if (mainTaskExists == null)
                    return new NotFoundObjectResult(new { message = "MainTask not found." });

                var subTasks = await _subTasksCollection.Find(st => st.MainTaskId == id).ToListAsync();
                return new OkObjectResult(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to fetch subtasks.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }

        // DELETE /api/maintasks/{id} - Delete a main task
        [Function("MainTasks_Delete")]
        public async Task<IActionResult> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/maintasks/{id}")] HttpRequest req,
            string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return new BadRequestObjectResult(new { message = "Invalid ID format." });

            try
            {
                var result = await _mainTasksCollection.DeleteOneAsync(mt => mt.Id == id);
                if (result.DeletedCount == 0)
                    return new NotFoundObjectResult(new { message = "MainTask not found." });

                return new ObjectResult(new { message = "MainTask Deleted." })
                {
                    StatusCode = StatusCodes.Status200OK
                };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to delete main task.", detail = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
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

        // DTO for creating main tasks
        public class CreateMainTaskDto
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? ProjectId { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        // DTO for updating main tasks
        public class UpdateMainTaskDto
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }
    }
}

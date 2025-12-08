using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using MainTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.MainTask;
using SubTaskModel = Flowboard_Project_Management_System_Backend.Models.FlowboardModel.SubTask;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/maintasks")]
    [Authorize] // Protect all endpoints with JWT
    public class MainTasksController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;
        private readonly IMongoCollection<MainTaskModel> _mainTasksCollection;
        private readonly IMongoCollection<SubTaskModel> _subTasksCollection;

        public MainTasksController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
            _mainTasksCollection = _mongoDbService.GetCollection<MainTaskModel>("maintasks");
            _subTasksCollection = _mongoDbService.GetCollection<SubTaskModel>("subtasks");
        }

            // PUT /api/maintasks/{id} - Update a main task
            [HttpPut("{id}")]
            [Authorize(Policy = "DetailedTaskEdit")]
            public async Task<IActionResult> Update(string id, [FromBody] UpdateMainTaskDto dto)
            {
                if (!ObjectId.TryParse(id, out _))
                    return BadRequest(new { message = "Invalid main task ID format." });
                if (dto == null)
                    return BadRequest(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });
                if (string.IsNullOrWhiteSpace(dto.Title))
                    return BadRequest(new { message = "Title is required." });

                var update = Builders<MainTaskModel>.Update
                    .Set(x => x.Title, dto.Title)
                    .Set(x => x.Description, dto.Description);

                var result = await _mainTasksCollection.UpdateOneAsync(x => x.Id == id, update);
                if (result.MatchedCount == 0)
                    return NotFound(new { message = "MainTask not found." });
                return NoContent();
            }

        // GET /api/maintasks - Get all main tasks
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var mainTasks = await _mainTasksCollection.Find(_ => true).ToListAsync();
                return Ok(mainTasks ?? new List<MainTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Unexpected server error.", detail = ex.Message });
            }
        }

        // GET /api/maintasks/project/{projectId} - Get main tasks by project
        [HttpGet("project/{projectId}")]
        public async Task<IActionResult> GetByProject(string projectId)
        {
            if (!ObjectId.TryParse(projectId, out _))
                return BadRequest(new { message = "Invalid project ID format." });

            try
            {
                var mainTasks = await _mainTasksCollection.Find(mt => mt.ProjectId == projectId).ToListAsync();
                return Ok(mainTasks ?? new List<MainTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch main tasks for project.", detail = ex.Message });
            }
        }

        // GET /api/maintasks/{id} - Get main task by ID
        [HttpGet("{id}", Name = "GetMainTaskById")]
        public async Task<IActionResult> GetById(string id)
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return BadRequest(new { message = "Invalid main task ID format." });

            try
            {
                var mainTask = await _mainTasksCollection.Find(mt => mt.Id == id).FirstOrDefaultAsync();
                return mainTask == null
                    ? NotFound(new { message = "MainTask not found." })
                    : Ok(mainTask);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch main task.", detail = ex.Message });
            }
        }

        // POST /api/maintasks - Create a new main task
        [HttpPost]
        [Authorize(Policy = "DetailedTaskCreate")]
        public async Task<IActionResult> Create([FromBody] CreateMainTaskDto mainTaskDto)
        {
            if (mainTaskDto == null)
                return BadRequest(new { message = "Invalid JSON or null body. Ensure Content-Type: application/json." });

            if (string.IsNullOrWhiteSpace(mainTaskDto.Title))
                return BadRequest(new { message = "Title is required." });

            try
            {
                var mainTask = new MainTaskModel
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Title = mainTaskDto.Title,
                    Description = mainTaskDto.Description,
                    ProjectId = mainTaskDto.ProjectId,
                    CreatedAt = DateTime.UtcNow
                };

                await _mainTasksCollection.InsertOneAsync(mainTask);
                return CreatedAtRoute("GetMainTaskById", new { id = mainTask.Id }, mainTask);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create main task.", detail = ex.Message });
            }
        }

        // GET /api/maintasks/{id}/subtasks - Get all subtasks for a main task
        [HttpGet("{id}/subtasks")]
        public async Task<IActionResult> GetSubTasks(string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid main task ID format." });

            try
            {
                var mainTaskExists = await _mainTasksCollection.Find(mt => mt.Id == id).FirstOrDefaultAsync();
                if (mainTaskExists == null)
                    return NotFound(new { message = "MainTask not found." });

                var subTasks = await _subTasksCollection.Find(st => st.MainTaskId == id).ToListAsync();
                return Ok(subTasks ?? new List<SubTaskModel>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch subtasks.", detail = ex.Message });
            }
        }

        // DELETE /api/maintasks/{id} - Delete a main task
        [HttpDelete("{id}")]
        [Authorize(Policy = "DetailedTaskDelete")]
        public async Task<IActionResult> Delete(string id)
        {
            if (!ObjectId.TryParse(id, out _))
                return BadRequest(new { message = "Invalid ID format." });

            try
            {
                var result = await _mainTasksCollection.DeleteOneAsync(mt => mt.Id == id);
                if (result.DeletedCount == 0)
                    return NotFound(new { message = "MainTask not found." });

                return StatusCode(200, new { message = "MainTask Deleted." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete main task.", detail = ex.Message });
            }
        }

        // DTO for creating main tasks
        public class CreateMainTaskDto
        {
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? ProjectId { get; set; }
        }

            // DTO for updating main tasks
            public class UpdateMainTaskDto
            {
                public string Title { get; set; }
                public string Description { get; set; }
            }

    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Flowboard_Project_Management_System_Backend.Services;
using Flowboard_Project_Management_System_Backend.Models;
using MongoDB.Driver;
using System.Security.Claims;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly IAnalyticsService _analytics;
        private readonly MongoDbService _mongoDbService;

        public AnalyticsController(IAnalyticsService analytics, MongoDbService mongoDbService)
        {
            _analytics = analytics;
            _mongoDbService = mongoDbService;
        }

    [HttpGet("summary")]
    [Authorize] // ensure only authenticated users (optionally Admin or ProjectMember)
    public async Task<IActionResult> Summary() => Ok(await _analytics.GetSummaryAsync());

    [HttpGet("projects/{projectId}/stats")]
    [Authorize]
    public async Task<IActionResult> ProjectStats(string projectId)
    {
        // Clients can only view stats for projects they're assigned to
        if (User.IsInRole("Client"))
        {
            var db = _mongoDbService.GetDatabase();
            var projectsCollection = db.GetCollection<FlowModels.Project>("project");
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
            if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                return StatusCode(403, new { message = "You do not have permission to view analytics for this project." });
        }

        var stats = await _analytics.GetProjectStatsAsync(projectId);
        return Ok(stats);
    }

    [HttpGet("users/{userId}/overview")]
    [Authorize]
    public async Task<IActionResult> UserOverview(string userId)
    {
        // optionally allow users to see only their own overview unless admin
        var overview = await _analytics.GetUserOverviewAsync(userId);
        return Ok(overview);
    }

    [HttpGet("tasks/timeline")]
    [Authorize]
    public async Task<IActionResult> Timeline([FromQuery] int days = 30)
    {
        var data = await _analytics.GetTasksTimelineAsync(days);
        return Ok(data);
    }

    [HttpGet("top-performers")]
    [Authorize]
    public async Task<IActionResult> TopPerformers([FromQuery] int limit = 5)
    {
        var data = await _analytics.GetTopPerformersAsync(limit);
        return Ok(data);
    }

        [HttpGet("kanban/{projectId}")]
        [Authorize]
        public async Task<IActionResult> Kanban(string projectId)
        {
            // Clients can only view kanban stats for projects they're assigned to
            if (User.IsInRole("Client"))
            {
                var db = _mongoDbService.GetDatabase();
                var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                
                var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                    return StatusCode(403, new { message = "You do not have permission to view kanban stats for this project." });
            }

            var data = await _analytics.GetKanbanStatsAsync(projectId);
            return Ok(data);
        }
    }
}

using System.Security.Claims;
using System.Threading.Tasks;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MongoDB.Driver;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/AnalyticsController.cs (see HomeFunctions.cs for the mapping rules
    /// this follows).
    ///
    /// Original: [Route("api/[controller]")] -> "api/analytics", 8x method-level [Authorize]
    /// with no policy - all become default-deny protected functions (no [AllowAnonymous]).
    ///
    /// User identity: ASP.NET Core's ControllerBase.User is replaced by req.HttpContext.User,
    /// which JwtAuthMiddleware populates with the validated ClaimsPrincipal (including the role
    /// claim) before this code runs - see Flowboard.Functions/Middleware/JwtAuthMiddleware.cs.
    /// </summary>
    public class AnalyticsFunctions
    {
        private readonly IAnalyticsService _analytics;
        private readonly MongoDbService _mongoDbService;

        public AnalyticsFunctions(IAnalyticsService analytics, MongoDbService mongoDbService)
        {
            _analytics = analytics;
            _mongoDbService = mongoDbService;
        }

        // GET /api/analytics/summary?projectId=
        [Function("Analytics_Summary")]
        public async Task<IActionResult> Summary(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/summary")] HttpRequest req)
        {
            var user = req.HttpContext.User;
            string? projectId = req.Query["projectId"];

            if (!string.IsNullOrEmpty(projectId) && user.IsInRole("Client"))
            {
                var db = _mongoDbService.GetDatabase();
                var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                    return new ObjectResult(new { message = "You do not have permission to view analytics for this project." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
            }

            return new OkObjectResult(await _analytics.GetSummaryAsync(projectId));
        }

        // GET /api/analytics/projects/{projectId}/stats
        [Function("Analytics_ProjectStats")]
        public async Task<IActionResult> ProjectStats(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/projects/{projectId}/stats")] HttpRequest req,
            string projectId)
        {
            var user = req.HttpContext.User;

            if (user.IsInRole("Client"))
            {
                var db = _mongoDbService.GetDatabase();
                var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                    return new ObjectResult(new { message = "You do not have permission to view analytics for this project." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
            }

            var stats = await _analytics.GetProjectStatsAsync(projectId);
            return new OkObjectResult(stats);
        }

        // GET /api/analytics/users/{userId}/overview
        [Function("Analytics_UserOverview")]
        public async Task<IActionResult> UserOverview(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/users/{userId}/overview")] HttpRequest req,
            string userId)
        {
            var overview = await _analytics.GetUserOverviewAsync(userId);
            return new OkObjectResult(overview);
        }

        // GET /api/analytics/tasks/timeline?days=30
        [Function("Analytics_Timeline")]
        public async Task<IActionResult> Timeline(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/tasks/timeline")] HttpRequest req)
        {
            var days = 30;
            if (int.TryParse(req.Query["days"], out var parsedDays))
                days = parsedDays;

            var data = await _analytics.GetTasksTimelineAsync(days);
            return new OkObjectResult(data);
        }

        // GET /api/analytics/top-performers?limit=5
        [Function("Analytics_TopPerformers")]
        public async Task<IActionResult> TopPerformers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/top-performers")] HttpRequest req)
        {
            var limit = 5;
            if (int.TryParse(req.Query["limit"], out var parsedLimit))
                limit = parsedLimit;

            var data = await _analytics.GetTopPerformersAsync(limit);
            return new OkObjectResult(data);
        }

        // GET /api/analytics/kanban/{projectId}
        [Function("Analytics_Kanban")]
        public async Task<IActionResult> Kanban(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/kanban/{projectId}")] HttpRequest req,
            string projectId)
        {
            var user = req.HttpContext.User;

            if (user.IsInRole("Client"))
            {
                var db = _mongoDbService.GetDatabase();
                var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                if (project == null || project?.Permissions == null || (userId != null && !project.Permissions.ContainsKey(userId)))
                    return new ObjectResult(new { message = "You do not have permission to view kanban stats for this project." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
            }

            var data = await _analytics.GetKanbanStatsAsync(projectId);
            return new OkObjectResult(data);
        }

        // GET /api/analytics/progress?projectId=&userId=
        [Function("Analytics_TaskProgress")]
        public async Task<IActionResult> TaskProgress(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/progress")] HttpRequest req)
        {
            var user = req.HttpContext.User;
            string? projectId = req.Query["projectId"];
            string? userId = req.Query["userId"];

            // If userId is provided and not the current user, check if admin
            if (!string.IsNullOrEmpty(userId))
            {
                var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userId != currentUserId && !user.IsInRole("Admin"))
                {
                    return new ObjectResult(new { message = "You can only view your own task progress." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                }
            }
            else
            {
                // Default to current user
                userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }

            // If projectId is provided and user is client, verify access
            if (!string.IsNullOrEmpty(projectId) && user.IsInRole("Client"))
            {
                var db = _mongoDbService.GetDatabase();
                var projectsCollection = db.GetCollection<FlowModels.Project>("project");
                var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
                if (project == null || project?.Permissions == null || (currentUserId != null && !project.Permissions.ContainsKey(currentUserId)))
                    return new ObjectResult(new { message = "You do not have permission to view this project's analytics." })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
            }

            var progress = await _analytics.GetTaskProgressAsync(projectId, userId);
            return new OkObjectResult(progress);
        }

        // GET /api/analytics/my-progress
        [Function("Analytics_MyProgress")]
        public async Task<IActionResult> MyProgress(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/analytics/my-progress")] HttpRequest req)
        {
            var user = req.HttpContext.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return new BadRequestObjectResult(new { message = "User ID not found." });

            var progress = await _analytics.GetTaskProgressAsync(null, userId);
            return new OkObjectResult(progress);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using MongoDB.Driver;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.DTOs;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard_Project_Management_System_Backend.Services
{
    public interface IAnalyticsService
    {
        Task<SummaryDto> GetSummaryAsync(string? projectId = null);
        Task<ProjectStatsDto> GetProjectStatsAsync(string projectId);
        Task<UserOverviewDto> GetUserOverviewAsync(string userId);
        Task<IEnumerable<TimeseriesPoint>> GetTasksTimelineAsync(int days);
        Task<IEnumerable<TopPerformerDto>> GetTopPerformersAsync(int limit);
        Task<object> GetKanbanStatsAsync(string projectId);
        Task<TaskProgressDto> GetTaskProgressAsync(string? projectId = null, string? userId = null);
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly MongoDbService _mongoDbService;

        public AnalyticsService(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        public async Task<SummaryDto> GetSummaryAsync(string? projectId = null)
        {
            var db = _mongoDbService.GetDatabase();
            var usersCollection = db.GetCollection<FlowModels.User>("users");
            var projectsCollection = db.GetCollection<FlowModels.Project>("project");
            var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("maintasks");
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");

            // Filter by projectId if provided
            var filterBuilder = Builders<FlowModels.SubTask>.Filter;
            var subTaskFilter = string.IsNullOrEmpty(projectId) 
                ? filterBuilder.Empty 
                : filterBuilder.Eq(t => t.ProjectId, projectId);

            var mainTaskFilter = string.IsNullOrEmpty(projectId)
                ? Builders<FlowModels.MainTask>.Filter.Empty
                : Builders<FlowModels.MainTask>.Filter.Eq(t => t.ProjectId, projectId);

            // For project-specific analytics, we don't need total users
            var totalUsers = 0;
            var totalProjects = string.IsNullOrEmpty(projectId) ? (int)await projectsCollection.CountDocumentsAsync(_ => true) : 1;
            var totalMainTasks = (int)await mainTasksCollection.CountDocumentsAsync(mainTaskFilter);
            
            var allSubTasks = await subTasksCollection.Find(subTaskFilter).ToListAsync();
            var totalSubTasks = allSubTasks.Count;

            // Count tasks by status
            var tasksCompleted = allSubTasks.Count(t => t.Status?.ToLower() == "done" || t.Status?.ToLower() == "completed");
            var tasksInProgress = allSubTasks.Count(t => t.Status?.ToLower() == "in progress");
            var tasksToDo = allSubTasks.Count(t => t.Status?.ToLower() == "to do" || string.IsNullOrEmpty(t.Status));
            var tasksBlocked = allSubTasks.Count(t => t.Status?.ToLower() == "blocked");
            var tasksPending = totalSubTasks - tasksCompleted;

            // Count overdue tasks
            var now = DateTime.UtcNow;
            var tasksOverdue = allSubTasks.Count(t => 
                t.EndDate.HasValue && 
                t.EndDate.Value < now && 
                t.Status?.ToLower() != "done" && 
                t.Status?.ToLower() != "completed"
            );

            // Count active projects (projects with at least one task)
            var projectsWithTasks = allSubTasks.Select(t => t.ProjectId).Distinct().ToList();
            var activeProjects = projectsWithTasks.Count;

            // Group by status
            var tasksByStatus = allSubTasks
                .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "To Do" : t.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            // Group by priority
            var tasksByPriority = allSubTasks
                .GroupBy(t => string.IsNullOrEmpty(t.Priority) ? "Medium" : t.Priority)
                .ToDictionary(g => g.Key, g => g.Count());

            return new SummaryDto(
                TotalUsers: totalUsers,
                TotalProjects: totalProjects,
                TotalMainTasks: totalMainTasks,
                TotalSubTasks: totalSubTasks,
                TasksCompleted: tasksCompleted,
                TasksPending: tasksPending,
                TasksOverdue: tasksOverdue,
                ActiveProjects: activeProjects,
                TasksInProgress: tasksInProgress,
                TasksToDo: tasksToDo,
                TasksBlocked: tasksBlocked,
                TasksByStatus: tasksByStatus,
                TasksByPriority: tasksByPriority
            );
        }

        public async Task<ProjectStatsDto> GetProjectStatsAsync(string projectId)
        {
            var db = _mongoDbService.GetDatabase();
            var projectsCollection = db.GetCollection<FlowModels.Project>("project");
            var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("maintasks");
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");

            // Get the project
            var project = await projectsCollection.Find(p => p.Id == projectId).FirstOrDefaultAsync();
            if (project == null)
            {
                throw new Exception("Project not found");
            }

            // Get team member count
            var memberCount = project.TeamMembers?.Count ?? 0;

            // Get main tasks count for this project
            var mainTaskCount = (int)await mainTasksCollection.CountDocumentsAsync(m => m.ProjectId == projectId);

            // Get all subtasks for this project
            var tasks = await subTasksCollection.Find(s => s.ProjectId == projectId).ToListAsync();
            var subTaskCount = tasks.Count;
            var completedTasks = tasks.Count(t => t.Status?.ToLower() == "done" || t.Status?.ToLower() == "completed");
            
            var now = DateTime.UtcNow;
            var overdueTasks = tasks.Count(t => 
                t.EndDate.HasValue && 
                t.EndDate.Value < now && 
                t.Status?.ToLower() != "done" && 
                t.Status?.ToLower() != "completed"
            );

            var tasksByPriority = tasks
                .GroupBy(t => string.IsNullOrEmpty(t.Priority) ? "medium" : t.Priority.ToLower())
                .ToDictionary(g => g.Key, g => g.Count());

            var tasksByStatus = tasks
                .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "to Do" : t.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            // Get tasks by category
            var categories = await categoriesCollection.Find(c => c.ProjectId == projectId).ToListAsync();
            var tasksByCategory = new List<CategoryStats>();
            
            foreach (var category in categories)
            {
                var categoryTasks = tasks.Where(t => t.CategoryId == category.Id).ToList();
                var categoryTaskCount = categoryTasks.Count;
                var completedCategoryTasks = categoryTasks.Count(t => t.Status?.ToLower() == "done" || t.Status?.ToLower() == "completed");
                
                if (categoryTaskCount > 0)
                {
                    tasksByCategory.Add(new CategoryStats(
                        CategoryName: category.CategoryName ?? "Uncategorized",
                        TotalTasks: categoryTaskCount,
                        CompletedTasks: completedCategoryTasks
                    ));
                }
            }

            var completionRate = subTaskCount > 0 ? (double)completedTasks / subTaskCount : 0;

            return new ProjectStatsDto(
                ProjectId: projectId,
                ProjectName: project.ProjectName ?? "Project",
                MemberCount: memberCount,
                MainTaskCount: mainTaskCount,
                SubTaskCount: subTaskCount,
                CompletedSubTasks: completedTasks,
                OverdueSubTasks: overdueTasks,
                TasksByPriority: tasksByPriority,
                TasksByStatus: tasksByStatus,
                CompletionRate: completionRate,
                TasksByCategory: tasksByCategory
            );
        }

        public async Task<UserOverviewDto> GetUserOverviewAsync(string userId)
        {
            var db = _mongoDbService.GetDatabase();
            var usersCollection = db.GetCollection<FlowModels.User>("users");
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            
            var user = await usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            var tasks = await subTasksCollection.Find(t => t.AssignedTo != null && t.AssignedTo.Contains(userId)).ToListAsync();
            
            var assignedTasks = tasks.Count;
            var completedTasks = tasks.Count(t => t.Status?.ToLower() == "done" || t.Status?.ToLower() == "completed");
            var pendingTasks = assignedTasks - completedTasks;
            
            var now = DateTime.UtcNow;
            var overdueTasks = tasks.Count(t => 
                t.EndDate.HasValue && 
                t.EndDate.Value < now && 
                t.Status?.ToLower() != "done" && 
                t.Status?.ToLower() != "completed"
            );

            var completionRate = assignedTasks > 0 ? Math.Round((double)completedTasks / assignedTasks * 100, 2) : 0;
            
            return new UserOverviewDto(
                UserId: userId,
                UserName: user?.UserName ?? "Unknown",
                AssignedTasks: assignedTasks,
                CompletedTasks: completedTasks,
                PendingTasks: pendingTasks,
                OverdueTasks: overdueTasks,
                CompletionRate: completionRate,
                ProjectSummaries: new List<ProjectTaskSummary>()
            );
        }

        public async Task<IEnumerable<TimeseriesPoint>> GetTasksTimelineAsync(int days)
        {
            var result = new List<TimeseriesPoint>();
            for (int i = 0; i < days; i++)
            {
                result.Add(new TimeseriesPoint(
                    Date: DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd"),
                    Created: 0,
                    Completed: 0,
                    InProgress: 0,
                    Total: 0
                ));
            }
            return await Task.FromResult(result);
        }

        public async Task<IEnumerable<TopPerformerDto>> GetTopPerformersAsync(int limit)
        {
            var result = new List<TopPerformerDto>();
            return await Task.FromResult(result.AsEnumerable());
        }

        public async Task<object> GetKanbanStatsAsync(string projectId)
        {
            var db = _mongoDbService.GetDatabase();
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var tasks = await subTasksCollection.Find(s => s.ProjectId == projectId).ToListAsync();
            
            var statusCounts = tasks
                .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "To Do" : t.Status)
                .ToDictionary(g => g.Key, g => g.Count());
            
            return new { 
                Total = tasks.Count,
                StatusBreakdown = statusCounts
            };
        }

        public async Task<TaskProgressDto> GetTaskProgressAsync(string? projectId = null, string? userId = null)
        {
            var db = _mongoDbService.GetDatabase();
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            
            // Build filter
            var filterBuilder = Builders<FlowModels.SubTask>.Filter;
            var filter = filterBuilder.Empty;
            
            if (!string.IsNullOrEmpty(projectId))
                filter = filter & filterBuilder.Eq(t => t.ProjectId, projectId);
            
            if (!string.IsNullOrEmpty(userId))
                filter = filter & filterBuilder.AnyEq(t => t.AssignedTo, userId);
            
            var tasks = await subTasksCollection.Find(filter).ToListAsync();
            var totalTasks = tasks.Count;

            if (totalTasks == 0)
            {
                return new TaskProgressDto(
                    TotalTasks: 0,
                    CompletedTasks: 0,
                    InProgressTasks: 0,
                    ToDoTasks: 0,
                    BlockedTasks: 0,
                    OverdueTasks: 0,
                    CompletionPercentage: 0,
                    InProgressPercentage: 0,
                    RemainingTasks: 0,
                    StatusBreakdown: new Dictionary<string, int>()
                );
            }

            var completedTasks = tasks.Count(t => t.Status?.ToLower() == "done" || t.Status?.ToLower() == "completed");
            var inProgressTasks = tasks.Count(t => t.Status?.ToLower() == "in progress");
            var toDoTasks = tasks.Count(t => t.Status?.ToLower() == "to do" || string.IsNullOrEmpty(t.Status));
            var blockedTasks = tasks.Count(t => t.Status?.ToLower() == "blocked");
            
            var now = DateTime.UtcNow;
            var overdueTasks = tasks.Count(t => 
                t.EndDate.HasValue && 
                t.EndDate.Value < now && 
                t.Status?.ToLower() != "done" && 
                t.Status?.ToLower() != "completed"
            );

            var remainingTasks = totalTasks - completedTasks;
            var completionPercentage = Math.Round((double)completedTasks / totalTasks * 100, 2);
            var inProgressPercentage = Math.Round((double)inProgressTasks / totalTasks * 100, 2);

            var statusBreakdown = tasks
                .GroupBy(t => string.IsNullOrEmpty(t.Status) ? "To Do" : t.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            return new TaskProgressDto(
                TotalTasks: totalTasks,
                CompletedTasks: completedTasks,
                InProgressTasks: inProgressTasks,
                ToDoTasks: toDoTasks,
                BlockedTasks: blockedTasks,
                OverdueTasks: overdueTasks,
                CompletionPercentage: completionPercentage,
                InProgressPercentage: inProgressPercentage,
                RemainingTasks: remainingTasks,
                StatusBreakdown: statusBreakdown
            );
        }
    }
}

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
        Task<SummaryDto> GetSummaryAsync();
        Task<ProjectStatsDto> GetProjectStatsAsync(string projectId);
        Task<UserOverviewDto> GetUserOverviewAsync(string userId);
        Task<IEnumerable<TimeseriesPoint>> GetTasksTimelineAsync(int days);
        Task<IEnumerable<TopPerformerDto>> GetTopPerformersAsync(int limit);
        Task<object> GetKanbanStatsAsync(string projectId);
    }

    public class AnalyticsService : IAnalyticsService
    {
        private readonly MongoDbService _mongoDbService;

        public AnalyticsService(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        public async Task<SummaryDto> GetSummaryAsync()
        {
            var db = _mongoDbService.GetDatabase();
            var usersCollection = db.GetCollection<FlowModels.User>("users");
            var projectsCollection = db.GetCollection<FlowModels.Project>("project");
            var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("maintasks");
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");

            var totalUsers = (int)await usersCollection.CountDocumentsAsync(_ => true);
            var totalProjects = (int)await projectsCollection.CountDocumentsAsync(_ => true);
            var totalMainTasks = (int)await mainTasksCollection.CountDocumentsAsync(_ => true);
            var totalSubTasks = (int)await subTasksCollection.CountDocumentsAsync(_ => true);

            return new SummaryDto(
                TotalUsers: totalUsers,
                TotalProjects: totalProjects,
                TotalMainTasks: totalMainTasks,
                TotalSubTasks: totalSubTasks,
                TasksCompleted: 0,
                TasksPending: 0,
                TasksOverdue: 0,
                ActiveProjects: 0
            );
        }

        public async Task<ProjectStatsDto> GetProjectStatsAsync(string projectId)
        {
            var db = _mongoDbService.GetDatabase();
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var subTaskCount = (int)await subTasksCollection.CountDocumentsAsync(s => s.ProjectId == projectId);

            return new ProjectStatsDto(
                ProjectId: projectId,
                ProjectName: "Project",
                MemberCount: 0,
                MainTaskCount: 0,
                SubTaskCount: subTaskCount,
                CompletedSubTasks: 0,
                OverdueSubTasks: 0,
                TasksByPriority: new { }
            );
        }

        public async Task<UserOverviewDto> GetUserOverviewAsync(string userId)
        {
            var db = _mongoDbService.GetDatabase();
            var usersCollection = db.GetCollection<FlowModels.User>("users");
            var user = await usersCollection.Find(u => u.Id == userId).FirstOrDefaultAsync();
            
            return new UserOverviewDto(
                UserId: userId,
                UserName: user?.UserName ?? "Unknown",
                AssignedTasks: 0,
                CompletedTasks: 0,
                PendingTasks: 0,
                Assignments: new List<object>()
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
                    Completed: 0
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
            var count = await subTasksCollection.CountDocumentsAsync(s => s.ProjectId == projectId);
            
            return new { Total = count };
        }
    }
}

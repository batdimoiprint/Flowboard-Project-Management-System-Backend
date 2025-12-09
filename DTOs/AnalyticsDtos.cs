namespace Flowboard_Project_Management_System_Backend.DTOs;

public record SummaryDto(
    int TotalUsers,
    int TotalProjects,
    int TotalMainTasks,
    int TotalSubTasks,
    int TasksCompleted,
    int TasksPending,
    int TasksOverdue,
    int ActiveProjects,
    int TasksInProgress,
    int TasksToDo,
    int TasksBlocked,
    Dictionary<string, int> TasksByStatus,
    Dictionary<string, int> TasksByPriority
);

public record ProjectStatsDto(
    string ProjectId,
    string ProjectName,
    int MemberCount,
    int MainTaskCount,
    int SubTaskCount,
    int CompletedSubTasks,
    int OverdueSubTasks,
    Dictionary<string, int> TasksByPriority,
    Dictionary<string, int> TasksByStatus,
    double CompletionRate,
    List<CategoryStats> TasksByCategory
);

public record CategoryStats(
    string CategoryName,
    int TotalTasks,
    int CompletedTasks
);

public record UserOverviewDto(
    string UserId,
    string UserName,
    int AssignedTasks,
    int CompletedTasks,
    int PendingTasks,
    int OverdueTasks,
    double CompletionRate,
    List<ProjectTaskSummary> ProjectSummaries
);

public record ProjectTaskSummary(
    string ProjectId,
    string ProjectName,
    int TotalTasks,
    int CompletedTasks
);

public record TimeseriesPoint(
    string Date, 
    int Created, 
    int Completed,
    int InProgress,
    int Total
);

public record TopPerformerDto(
    string UserId, 
    string UserName, 
    int CompletedCount,
    double CompletionRate
);

public record TaskProgressDto(
    int TotalTasks,
    int CompletedTasks,
    int InProgressTasks,
    int ToDoTasks,
    int BlockedTasks,
    int OverdueTasks,
    double CompletionPercentage,
    double InProgressPercentage,
    int RemainingTasks,
    Dictionary<string, int> StatusBreakdown
);

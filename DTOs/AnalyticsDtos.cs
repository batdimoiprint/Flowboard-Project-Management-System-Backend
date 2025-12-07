namespace Flowboard_Project_Management_System_Backend.DTOs;

public record SummaryDto(
    int TotalUsers,
    int TotalProjects,
    int TotalMainTasks,
    int TotalSubTasks,
    int TasksCompleted,
    int TasksPending,
    int TasksOverdue,
    int ActiveProjects
);

public record ProjectStatsDto(
    string ProjectId,
    string ProjectName,
    int MemberCount,
    int MainTaskCount,
    int SubTaskCount,
    int CompletedSubTasks,
    int OverdueSubTasks,
    object TasksByPriority // { Critical: 5, High: 10, ... }
);

public record UserOverviewDto(
    string UserId,
    string UserName,
    int AssignedTasks,
    int CompletedTasks,
    int PendingTasks,
    IEnumerable<object> Assignments // small list of active tasks
);

public record TimeseriesPoint(string Date, int Created, int Completed);

public record TopPerformerDto(string UserId, string UserName, int CompletedCount);

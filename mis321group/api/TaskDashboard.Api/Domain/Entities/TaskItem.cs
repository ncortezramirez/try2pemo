namespace TaskDashboard.Api.Domain.Entities;

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3
}

public enum TaskItemStatus
{
    Todo = 0,
    InProgress = 1,
    Done = 2
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public TaskPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskItemStatus Status { get; set; }

    public int ProjectId { get; set; }

    // Always store timestamps in UTC.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

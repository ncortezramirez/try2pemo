namespace TaskDashboard.Api.Services;

/// <summary>
/// AI helpers backed by OpenAI (categorize tasks, break down work, suggest next step).
/// </summary>
public interface IAiService
{
    /// <summary>Suggest a category / project name from a task description.</summary>
    Task<AiCategorizeResult> CategorizeAsync(string taskDescription, IReadOnlyList<string>? existingProjectNames, CancellationToken cancellationToken = default);

    /// <summary>Split one large task into smaller subtasks.</summary>
    Task<AiBreakdownResult> BreakdownAsync(string taskTitle, string? taskDescription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pick the best next task from a list (may be empty when <see cref="AiNextContext.HasMeaningfulProjectContext"/> is true)
    /// and suggest new tasks that are not already on the list.
    /// </summary>
    Task<AiNextStepResult> RecommendNextAsync(
        IReadOnlyList<AiTaskSummary> tasks,
        AiNextContext? context,
        CancellationToken cancellationToken = default);
}

/// <summary>Input shape for "next task" — mirrors dashboard fields without requiring DB ids.</summary>
public sealed record AiTaskSummary(
    int? Id,
    string Title,
    string? Priority,
    string? Status,
    DateTime? DueDate,
    int? ProjectId = null,
    string? ProjectName = null,
    string? Description = null);

public sealed record AiProjectContext(string Name, string? Description, string? Category, string? GoalPurpose);

public sealed record AiNextContext(string? ProjectGoal, IReadOnlyList<AiProjectContext> Projects, bool HasMeaningfulProjectContext);

public sealed record AiCategorizeResult(bool Success, string? Error, string? SuggestedCategory, string? SuggestedProjectName, string? Reason);

public sealed record AiBreakdownResult(bool Success, string? Error, IReadOnlyList<string> Subtasks);

public sealed record AiSuggestedNewTask(string Title, string? Why, string? ProjectName = null);

public sealed record AiNextStepResult(
    bool Success,
    string? Error,
    int? RecommendedTaskId,
    string? RecommendedTitle,
    string? Rationale,
    IReadOnlyList<AiSuggestedNewTask>? SuggestedNewTasks = null);

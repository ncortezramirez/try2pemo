using Microsoft.AspNetCore.Mvc;
using TaskDashboard.Api.Services;

namespace TaskDashboard.Api.Controllers;

/// <summary>
/// AI endpoints powered by OpenAI (see OpenAiAiService). Requires OpenAI:ApiKey configuration.
/// </summary>
[ApiController]
[Route("api/ai")]
public class AiController(IAiService aiService) : ControllerBase
{
    private const int MaxNextTaskDescriptionForAi = 800;

    /// <summary>
    /// Suggest a category and project name from a task description.
    /// </summary>
    [HttpPost("categorize")]
    public async Task<IActionResult> Categorize([FromBody] CategorizeRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.TaskDescription))
        {
            return BadRequest(new { error = "taskDescription is required." });
        }

        var result = await aiService.CategorizeAsync(
            request.TaskDescription.Trim(),
            request.ExistingProjectNames,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        return Ok(new
        {
            suggestedCategory = result.SuggestedCategory,
            suggestedProjectName = result.SuggestedProjectName,
            reason = result.Reason
        });
    }

    /// <summary>
    /// Break a large task into smaller subtasks.
    /// </summary>
    [HttpPost("breakdown")]
    public async Task<IActionResult> Breakdown([FromBody] BreakdownRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.TaskTitle))
        {
            return BadRequest(new { error = "taskTitle is required." });
        }

        var result = await aiService.BreakdownAsync(
            request.TaskTitle.Trim(),
            request.TaskDescription?.Trim(),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        return Ok(new { subtasks = result.Subtasks });
    }

    /// <summary>
    /// Recommend the next task to work on from a list.
    /// </summary>
    [HttpPost("next")]
    public async Task<IActionResult> Next([FromBody] NextRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (request.Tasks is null)
        {
            return BadRequest(new { error = "tasks is required (use an empty array to only request new-task ideas from project context)." });
        }

        var projectContexts = request.Projects?
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new AiProjectContext(
                p.Name.Trim(),
                string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(),
                string.IsNullOrWhiteSpace(p.Category) ? null : p.Category.Trim(),
                string.IsNullOrWhiteSpace(p.GoalPurpose) ? null : p.GoalPurpose.Trim()))
            .ToList() ?? [];

        var hasMeaningfulProjectContext =
            !string.IsNullOrWhiteSpace(request.ProjectGoal) ||
            projectContexts.Any(p =>
                !string.IsNullOrWhiteSpace(p.GoalPurpose) ||
                !string.IsNullOrWhiteSpace(p.Description) ||
                !string.IsNullOrWhiteSpace(p.Category));

        var summaries = request.Tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t =>
            {
                var desc = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim();
                if (desc is not null && desc.Length > MaxNextTaskDescriptionForAi)
                {
                    desc = desc[..MaxNextTaskDescriptionForAi];
                }

                var projectName = string.IsNullOrWhiteSpace(t.ProjectName) ? null : t.ProjectName.Trim();
                return new AiTaskSummary(
                    t.Id,
                    t.Title.Trim(),
                    t.Priority,
                    t.Status,
                    t.DueDate,
                    t.ProjectId,
                    projectName,
                    desc);
            })
            .ToList();

        if (summaries.Count == 0 && !hasMeaningfulProjectContext)
        {
            return BadRequest(new { error = "Provide at least one task with a title, or project goal/description context for new-task suggestions." });
        }

        var context = new AiNextContext(
            string.IsNullOrWhiteSpace(request.ProjectGoal) ? null : request.ProjectGoal.Trim(),
            projectContexts,
            hasMeaningfulProjectContext);

        var result = await aiService.RecommendNextAsync(summaries, context, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return StatusCode(502, new { error = result.Error ?? "AI request failed." });
        }

        var suggestedNewTasks = (result.SuggestedNewTasks ?? Array.Empty<AiSuggestedNewTask>())
            .Select(s => new { title = s.Title, why = s.Why, projectName = s.ProjectName })
            .ToList();

        return Ok(new
        {
            recommendedTaskId = result.RecommendedTaskId,
            recommendedTitle = result.RecommendedTitle,
            rationale = result.Rationale,
            reason = result.Rationale,
            suggestedNewTasks
        });
    }

    public sealed class CategorizeRequest
    {
        public string TaskDescription { get; set; } = string.Empty;

        /// <summary>Optional names of projects the user already has (helps match naming).</summary>
        public List<string>? ExistingProjectNames { get; set; }
    }

    public sealed class BreakdownRequest
    {
        public string TaskTitle { get; set; } = string.Empty;
        public string? TaskDescription { get; set; }
    }

    public sealed class NextRequest
    {
        public List<NextTaskItem>? Tasks { get; set; }
        public string? ProjectGoal { get; set; }
        public List<NextProjectItem>? Projects { get; set; }
    }

    public sealed class NextTaskItem
    {
        public int? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Priority { get; set; }
        public string? Status { get; set; }
        public DateTime? DueDate { get; set; }

        /// <summary>Optional; ties the task to <see cref="NextProjectItem"/> entries for goal alignment.</summary>
        public int? ProjectId { get; set; }

        public string? ProjectName { get; set; }

        /// <summary>Optional task notes; long values are truncated server-side.</summary>
        public string? Description { get; set; }
    }

    public sealed class NextProjectItem
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? GoalPurpose { get; set; }
    }
}

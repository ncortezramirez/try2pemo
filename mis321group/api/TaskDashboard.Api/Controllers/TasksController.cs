using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System.Text;
using TaskDashboard.Api.Domain.Entities;
using TaskDashboard.Api.Infrastructure.Data;
using TaskDashboard.Api.Validation;

namespace TaskDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] TaskPriority? priority,
        [FromQuery] int? projectId,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] bool focusMode = false)
    {
        if (projectId.HasValue)
        {
            if (projectId.Value < 1)
            {
                return BadRequest(new { error = "projectId must be a positive integer." });
            }

        }

        try
        {
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();

            var filters = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT t.Id, t.Title, t.Description, t.Priority, t.DueDate, t.Status, t.ProjectId, t.CreatedAt, t.UpdatedAt, p.Name AS ProjectName
                FROM Tasks t
                JOIN Projects p ON p.Id = t.ProjectId
                """;

            if (priority.HasValue)
            {
                filters.Add("t.Priority = @priority");
                cmd.Parameters.AddWithValue("@priority", (int)priority.Value);
            }
            if (projectId.HasValue)
            {
                filters.Add("t.ProjectId = @projectId");
                cmd.Parameters.AddWithValue("@projectId", projectId.Value);
            }
            if (status.HasValue)
            {
                filters.Add("t.Status = @status");
                cmd.Parameters.AddWithValue("@status", (int)status.Value);
            }
            if (focusMode)
            {
                var endOfToday = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
                filters.Add("(t.Status <> @doneStatus AND t.DueDate IS NOT NULL AND t.DueDate <= @endOfToday AND (t.Priority = @highPriority OR t.Priority = @urgentPriority))");
                cmd.Parameters.AddWithValue("@doneStatus", (int)TaskItemStatus.Done);
                cmd.Parameters.AddWithValue("@highPriority", (int)TaskPriority.High);
                cmd.Parameters.AddWithValue("@urgentPriority", (int)TaskPriority.Urgent);
                cmd.Parameters.AddWithValue("@endOfToday", endOfToday);
            }
            if (filters.Count > 0)
            {
                cmd.CommandText += $" WHERE {string.Join(" AND ", filters)}";
            }
            cmd.CommandText += " ORDER BY t.Priority DESC, CASE WHEN t.DueDate IS NULL THEN 1 ELSE 0 END, t.DueDate, t.CreatedAt;";

            var tasks = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tasks.Add(ReadTaskRow(reader));
            }

            return Ok(tasks);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while loading tasks." });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllTasks(
        [FromQuery] TaskPriority? priority,
        [FromQuery] int? projectId,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] bool focusMode = false)
    {
        // "all" is an explicit endpoint for every task across projects.
        return await Get(priority, projectId, status, focusMode);
    }

    [HttpGet("next")]
    public async Task<IActionResult> GetNextTask()
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Title, t.Description, t.Priority, t.DueDate, t.Status, t.ProjectId, t.CreatedAt, t.UpdatedAt, p.Name AS ProjectName
            FROM Tasks t
            JOIN Projects p ON p.Id = t.ProjectId
            WHERE t.Status <> @doneStatus
            ORDER BY t.Priority DESC, CASE WHEN t.DueDate IS NULL THEN 1 ELSE 0 END, t.DueDate, t.CreatedAt
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@doneStatus", (int)TaskItemStatus.Done);

        await using var reader = await cmd.ExecuteReaderAsync();
        var hasRow = await reader.ReadAsync();

        if (!hasRow)
        {
            return Ok(new { message = "No active task found." });
        }

        var dueDate = reader.IsDBNull(reader.GetOrdinal("DueDate")) ? (DateTime?)null : reader.GetDateTime("DueDate");
        return Ok(new
        {
            Id = reader.GetInt32("Id"),
            Title = reader.GetString("Title"),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString("Description"),
            Priority = ((TaskPriority)reader.GetInt32("Priority")).ToString(),
            DueDate = dueDate,
            Status = ((TaskItemStatus)reader.GetInt32("Status")).ToString(),
            ProjectId = reader.GetInt32("ProjectId"),
            ProjectName = reader.GetString("ProjectName"),
            CreatedAt = reader.GetDateTime("CreatedAt"),
            UpdatedAt = reader.GetDateTime("UpdatedAt"),
            IsDueSoon = dueDate.HasValue && dueDate.Value <= DateTime.UtcNow.AddDays(3)
        });
    }

    [HttpGet("{id:int}/calendar")]
    public async Task<IActionResult> ExportTaskToCalendar(int id)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Title, t.Description, t.DueDate, p.Name AS ProjectName
            FROM Tasks t
            JOIN Projects p ON p.Id = t.ProjectId
            WHERE t.Id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return NotFound(new { error = "Task not found." });
        }

        if (reader.IsDBNull(reader.GetOrdinal("DueDate")))
        {
            return BadRequest(new { error = "Task does not have a due date to export." });
        }

        var dueDateUtc = reader.GetDateTime("DueDate").ToUniversalTime();
        var endDateUtc = dueDateUtc.AddHours(1);
        var nowUtc = DateTime.UtcNow;

        var ics = BuildIcsContent(
            uid: $"task-{reader.GetInt32("Id")}@taskdashboard",
            summary: reader.GetString("Title"),
            description: reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString("Description"),
            projectName: reader.GetString("ProjectName"),
            dtStampUtc: nowUtc,
            startUtc: dueDateUtc,
            endUtc: endDateUtc);

        var bytes = Encoding.UTF8.GetBytes(ics);
        var safeFileName = $"{SanitizeFileName(reader.GetString("Title"))}.ics";

        return File(bytes, "text/calendar", safeFileName);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (!TryParsePriority(request.Priority, out var parsedPriority, out var priorityError))
        {
            return BadRequest(new { error = priorityError });
        }
        if (!TryParseStatus(request.Status, out var parsedStatus, out var statusError))
        {
            return BadRequest(new { error = statusError });
        }

        var validationError = RequestValidators.ValidateTaskBody(request.Title, request.ProjectId, request.Description, request.DueDate);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var projectExists = await ProjectExistsAsync(request.ProjectId);
        if (!projectExists)
        {
            return BadRequest(new { error = "ProjectId does not match an existing project." });
        }

        try
        {
            var createdAt = DateTime.UtcNow;
            var updatedAt = createdAt;
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Tasks (Title, Description, Priority, DueDate, Status, ProjectId, CreatedAt, UpdatedAt)
                VALUES (@title, @description, @priority, @dueDate, @status, @projectId, @createdAt, @updatedAt);
                SELECT LAST_INSERT_ID();
                """;
            cmd.Parameters.AddWithValue("@title", request.Title!.Trim());
            cmd.Parameters.AddWithValue("@description", string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim());
            cmd.Parameters.AddWithValue("@priority", (int)parsedPriority);
            cmd.Parameters.AddWithValue("@dueDate", request.DueDate.HasValue ? request.DueDate.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@status", (int)parsedStatus);
            cmd.Parameters.AddWithValue("@projectId", request.ProjectId);
            cmd.Parameters.AddWithValue("@createdAt", createdAt);
            cmd.Parameters.AddWithValue("@updatedAt", updatedAt);

            var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            var createdTask = await GetTaskByIdAsync(newId);
            return Created($"/api/tasks/{newId}", createdTask);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while creating the task." });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        if (!TryParsePriority(request.Priority, out var parsedPriority, out var priorityError))
        {
            return BadRequest(new { error = priorityError });
        }
        if (!TryParseStatus(request.Status, out var parsedStatus, out var statusError))
        {
            return BadRequest(new { error = statusError });
        }

        var validationError = RequestValidators.ValidateTaskBody(request.Title, request.ProjectId, request.Description, request.DueDate);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var projectExists = await ProjectExistsAsync(request.ProjectId);
        if (!projectExists)
        {
            return BadRequest(new { error = "ProjectId does not match an existing project." });
        }

        try
        {
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            var updatedAt = DateTime.UtcNow;
            cmd.CommandText = """
                UPDATE Tasks
                SET Title = @title, Description = @description, Priority = @priority, DueDate = @dueDate, Status = @status, ProjectId = @projectId, UpdatedAt = @updatedAt
                WHERE Id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@title", request.Title!.Trim());
            cmd.Parameters.AddWithValue("@description", string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim());
            cmd.Parameters.AddWithValue("@priority", (int)parsedPriority);
            cmd.Parameters.AddWithValue("@dueDate", request.DueDate.HasValue ? request.DueDate.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@status", (int)parsedStatus);
            cmd.Parameters.AddWithValue("@projectId", request.ProjectId);
            cmd.Parameters.AddWithValue("@updatedAt", updatedAt);

            var changed = await cmd.ExecuteNonQueryAsync();
            if (changed == 0)
            {
                return NotFound(new { error = "Task not found." });
            }

            var updatedTask = await GetTaskByIdAsync(id);
            return Ok(updatedTask);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while updating the task." });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Tasks WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            var changed = await cmd.ExecuteNonQueryAsync();
            if (changed == 0)
            {
                return NotFound(new { error = "Task not found." });
            }
            return Ok(new { message = "Task deleted successfully." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while deleting the task." });
        }
    }

    public class TaskRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Priority { get; set; } = "medium";
        public DateTime? DueDate { get; set; }
        public string Status { get; set; } = "todo";
        public int ProjectId { get; set; }
    }

    private static string BuildIcsContent(
        string uid,
        string summary,
        string? description,
        string? projectName,
        DateTime dtStampUtc,
        DateTime startUtc,
        DateTime endUtc)
    {
        var details = string.IsNullOrWhiteSpace(projectName)
            ? description ?? string.Empty
            : $"Project: {projectName}\\n{description}";

        return string.Join("\r\n",
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//TaskDashboard//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{EscapeIcsText(uid)}",
            $"DTSTAMP:{dtStampUtc:yyyyMMdd'T'HHmmss'Z'}",
            $"DTSTART:{startUtc:yyyyMMdd'T'HHmmss'Z'}",
            $"DTEND:{endUtc:yyyyMMdd'T'HHmmss'Z'}",
            $"SUMMARY:{EscapeIcsText(summary)}",
            $"DESCRIPTION:{EscapeIcsText(details)}",
            "END:VEVENT",
            "END:VCALENDAR");
    }

    private static string EscapeIcsText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var clean = new string(value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(clean) ? "task" : clean.Trim();
    }

    private async Task<bool> ProjectExistsAsync(int projectId)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Projects WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", projectId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    private async Task<object?> GetTaskByIdAsync(int id)
    {
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Title, t.Description, t.Priority, t.DueDate, t.Status, t.ProjectId, t.CreatedAt, t.UpdatedAt, p.Name AS ProjectName
            FROM Tasks t
            JOIN Projects p ON p.Id = t.ProjectId
            WHERE t.Id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadTaskRow(reader);
    }

    private static object ReadTaskRow(MySqlDataReader reader)
    {
        var descriptionOrdinal = reader.GetOrdinal("Description");
        var dueOrdinal = reader.GetOrdinal("DueDate");
        return new
        {
            Id = reader.GetInt32("Id"),
            Title = reader.GetString("Title"),
            Description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString("Description"),
            Priority = ((TaskPriority)reader.GetInt32("Priority")).ToString(),
            DueDate = reader.IsDBNull(dueOrdinal) ? (DateTime?)null : reader.GetDateTime("DueDate"),
            Status = ((TaskItemStatus)reader.GetInt32("Status")).ToString(),
            ProjectId = reader.GetInt32("ProjectId"),
            ProjectName = reader.GetString("ProjectName"),
            CreatedAt = reader.GetDateTime("CreatedAt"),
            UpdatedAt = reader.GetDateTime("UpdatedAt")
        };
    }

    private static bool TryParsePriority(string? value, out TaskPriority priority, out string? error)
    {
        error = null;
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "low":
                priority = TaskPriority.Low;
                return true;
            case "medium":
                priority = TaskPriority.Medium;
                return true;
            case "high":
                priority = TaskPriority.High;
                return true;
            case "urgent":
                priority = TaskPriority.Urgent;
                return true;
            default:
                priority = TaskPriority.Medium;
                error = "Priority must be one of: low, medium, high, urgent.";
                return false;
        }
    }

    private static bool TryParseStatus(string? value, out TaskItemStatus status, out string? error)
    {
        error = null;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "todo":
                status = TaskItemStatus.Todo;
                return true;
            case "inprogress":
            case "in-progress":
                status = TaskItemStatus.InProgress;
                return true;
            case "done":
                status = TaskItemStatus.Done;
                return true;
            default:
                status = TaskItemStatus.Todo;
                error = "Status must be one of: todo, in-progress, done.";
                return false;
        }
    }
}

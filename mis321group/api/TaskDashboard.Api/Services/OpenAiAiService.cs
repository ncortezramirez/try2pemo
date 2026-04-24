using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskDashboard.Api.Services;

/// <summary>
/// Calls OpenAI Chat Completions API with structured prompts and JSON-only replies.
/// Configure: OpenAI:ApiKey (use User Secrets or environment variable in development).
/// </summary>
public sealed class OpenAiAiService : IAiService
{
    private const int MaxSuggestedNewTasks = 5;
    private const int MaxSuggestedNewTaskTitleLength = 500;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiAiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAiAiService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiAiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiCategorizeResult> CategorizeAsync(
        string taskDescription,
        IReadOnlyList<string>? existingProjectNames,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiCategorizeResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", null, null, null);
        }

        var systemPrompt = """
            You help organize tasks for a personal task dashboard.
            The user will send JSON with a task description and optional existing project names.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - suggestedCategory: string (short label, e.g. Education, Work, Personal)
            - suggestedProjectName: string (concise project name; prefer matching an existing name if it fits)
            - reason: string (one short sentence)
            """;

        var userPayload = JsonSerializer.Serialize(new
        {
            taskDescription,
            existingProjectNames = existingProjectNames ?? Array.Empty<string>()
        }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiCategorizeResult(false, "OpenAI did not return usable content.", null, null, null);
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            return new AiCategorizeResult(
                true,
                null,
                root.GetProperty("suggestedCategory").GetString(),
                root.GetProperty("suggestedProjectName").GetString(),
                root.GetProperty("reason").GetString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse categorize JSON: {Content}", content);
            return new AiCategorizeResult(false, "Could not parse AI response as expected JSON.", null, null, null);
        }
    }

    public async Task<AiBreakdownResult> BreakdownAsync(
        string taskTitle,
        string? taskDescription,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiBreakdownResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", Array.Empty<string>());
        }

        var systemPrompt = """
            You break large tasks into small, actionable subtasks for a student/professional dashboard.
            The user sends JSON with taskTitle and optional taskDescription.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - subtasks: array of strings (5–12 items, each one clear and doable in one sitting; start with verbs)
            """;

        var userPayload = JsonSerializer.Serialize(new { taskTitle, taskDescription }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiBreakdownResult(false, "OpenAI did not return usable content.", Array.Empty<string>());
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var arr = doc.RootElement.GetProperty("subtasks");
            var list = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }

            return new AiBreakdownResult(true, null, list);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse breakdown JSON: {Content}", content);
            return new AiBreakdownResult(false, "Could not parse AI response as expected JSON.", Array.Empty<string>());
        }
    }

    public async Task<AiNextStepResult> RecommendNextAsync(
        IReadOnlyList<AiTaskSummary> tasks,
        AiNextContext? context,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiNextStepResult(false, "OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings, User Secrets, or environment.", null, null, null);
        }

        var hasContext = context?.HasMeaningfulProjectContext ?? false;
        if (tasks.Count == 0 && !hasContext)
        {
            return new AiNextStepResult(false, "No tasks provided and no project context to infer new work from.", null, null, null);
        }

        var systemPrompt = """
            You help a unified task dashboard: (1) pick ONE next task from the user's existing list, and (2) suggest NEW tasks they have not added yet.
            Optimize for project outcomes, not just urgency.
            Always reason by project group first:
            1) Group tasks[] by projectName (or "Ungrouped" if missing).
            2) For each group, read task titles + descriptions to infer missing work.
            3) Suggest new tasks that clearly belong to a specific group.
            When tasks[] is non-empty: pick the best next task to work on now. Prefer not Done; alignment to project goal/purpose; higher priority; sooner due dates; overdue or stale work when relevant.
            Each task may include projectId, projectName, description — use these to match tasks to projects[] by name/id.
            When tasks[] is empty: set recommendedTaskId to null, recommendedTitle to "", rationale to one short sentence that you are suggesting starter tasks from project context, and suggestedNewTasks must have 3-6 concrete actionable items.
            When tasks[] is non-empty: recommendedTitle should match one existing task title exactly when possible. suggestedNewTasks: 0-5 items that are NOT duplicates of any existing task title (case-insensitive) and that fill obvious gaps toward project goals.
            The user sends JSON: { "tasks": [...], "projectGoal": string|null, "projects": [...], "hasMeaningfulProjectContext": boolean }.
            If hasMeaningfulProjectContext is false: still pick from the list if non-empty; keep suggestedNewTasks short or empty; start rationale with a brief nudge to add project goal/purpose for better ideas.
            Respond with ONLY a single JSON object (no markdown, no code fences) using exactly these keys:
            - recommendedTaskId: number or null
            - recommendedTitle: string (empty string allowed only when tasks[] is empty)
            - rationale: string (one or two short sentences)
            - suggestedNewTasks: array of { "title": string (max ~12 words, start with a verb when natural), "why": string (one short sentence), "projectName": string (must match a known project/group when possible) }
            """;

        var userPayload = JsonSerializer.Serialize(new
        {
            tasks,
            projectGoal = context?.ProjectGoal,
            projects = context?.Projects ?? Array.Empty<AiProjectContext>(),
            hasMeaningfulProjectContext = hasContext
        }, JsonOptions);

        var content = await CompleteChatJsonAsync(apiKey, systemPrompt, userPayload, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return new AiNextStepResult(false, "OpenAI did not return usable content.", null, null, null);
        }

        try
        {
            var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            int? id = null;
            if (root.TryGetProperty("recommendedTaskId", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                id = idEl.GetInt32();
            }

            var recommendedTitle = root.TryGetProperty("recommendedTitle", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
                ? titleEl.GetString()
                : null;
            var rationale = root.TryGetProperty("rationale", out var ratEl) && ratEl.ValueKind == JsonValueKind.String
                ? ratEl.GetString()
                : null;

            var suggested = ParseAndDedupeSuggestedNewTasks(root, tasks);

            return new AiNextStepResult(true, null, id, recommendedTitle, rationale, suggested);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse next-step JSON: {Content}", content);
            return new AiNextStepResult(false, "Could not parse AI response as expected JSON.", null, null, null);
        }
    }

    private static IReadOnlyList<AiSuggestedNewTask> ParseAndDedupeSuggestedNewTasks(JsonElement root, IReadOnlyList<AiTaskSummary> existingTasks)
    {
        var existingTitles = new HashSet<string>(
            existingTasks.Select(t => t.Title.Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("suggestedNewTasks", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AiSuggestedNewTask>();
        }

        var list = new List<AiSuggestedNewTask>();
        foreach (var el in arr.EnumerateArray())
        {
            if (list.Count >= MaxSuggestedNewTasks)
            {
                break;
            }

            string? title = null;
            string? why = null;
            string? projectName = null;
            if (el.ValueKind == JsonValueKind.String)
            {
                title = el.GetString();
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                {
                    title = tEl.GetString();
                }

                if (el.TryGetProperty("why", out var wEl) && wEl.ValueKind == JsonValueKind.String)
                {
                    why = wEl.GetString();
                }

                if (el.TryGetProperty("projectName", out var pEl) && pEl.ValueKind == JsonValueKind.String)
                {
                    projectName = pEl.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var trimmed = title.Trim();
            if (trimmed.Length > MaxSuggestedNewTaskTitleLength)
            {
                trimmed = trimmed[..MaxSuggestedNewTaskTitleLength];
            }

            if (existingTitles.Contains(trimmed))
            {
                continue;
            }

            if (list.Any(x => string.Equals(x.Title, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            existingTitles.Add(trimmed);
            why = string.IsNullOrWhiteSpace(why) ? null : why.Trim();
            if (why is { Length: > 400 })
            {
                why = why[..400];
            }

            projectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim();
            if (projectName is { Length: > 200 })
            {
                projectName = projectName[..200];
            }

            list.Add(new AiSuggestedNewTask(trimmed, why, projectName));
        }

        return list;
    }

    private string? GetApiKey() => _configuration["OpenAI:ApiKey"];

    private string? GetModel() => _configuration["OpenAI:Model"] ?? "gpt-4o-mini";

    private string GetBaseUrl() => (_configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1").TrimEnd('/');

    /// <summary>
    /// Sends chat completion request with JSON response format; returns parsed message content (JSON string).
    /// </summary>
    private async Task<string?> CompleteChatJsonAsync(
        string apiKey,
        string systemPrompt,
        string userContent,
        CancellationToken cancellationToken)
    {
        var model = GetModel();
        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            response_format = new { type = "json_object" },
            temperature = 0.3
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{GetBaseUrl()}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI HTTP request failed.");
            return null;
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI API error {Status}: {Body}", (int)response.StatusCode, responseText);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");
            var first = choices[0];
            var message = first.GetProperty("message");
            var content = message.GetProperty("content").GetString();
            return NormalizeModelJson(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI response: {Body}", responseText);
            return null;
        }
    }

    /// <summary>Strip optional ```json ... ``` wrapper if the model adds it despite response_format.</summary>
    private static string? NormalizeModelJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 0)
            {
                trimmed = trimmed[..end].Trim();
            }
        }

        return trimmed;
    }
}

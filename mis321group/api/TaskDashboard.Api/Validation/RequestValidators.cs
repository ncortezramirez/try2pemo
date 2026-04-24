namespace TaskDashboard.Api.Validation;

/// <summary>
/// Simple, explicit validation for API inputs. Keeps controllers readable and testable.
/// </summary>
public static class RequestValidators
{
    public const int MaxTaskTitleLength = 500;
    public const int MaxTaskDescriptionLength = 4000;
    public const int MaxProjectNameLength = 200;
    public const int MaxProjectCategoryLength = 100;
    public const int MaxProjectGoalPurposeLength = 2000;

    public static string? ValidateProjectBody(string? name, string? category, string? goalPurpose, bool categoryRequired)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (name.Trim().Length > MaxProjectNameLength)
        {
            return $"Name must be at most {MaxProjectNameLength} characters.";
        }

        if (categoryRequired && string.IsNullOrWhiteSpace(category))
        {
            return "Category is required.";
        }

        if (!string.IsNullOrWhiteSpace(category) && category.Trim().Length > MaxProjectCategoryLength)
        {
            return $"Category must be at most {MaxProjectCategoryLength} characters.";
        }

        if (string.IsNullOrWhiteSpace(goalPurpose))
        {
            return "GoalPurpose is required.";
        }

        if (goalPurpose.Trim().Length > MaxProjectGoalPurposeLength)
        {
            return $"GoalPurpose must be at most {MaxProjectGoalPurposeLength} characters.";
        }

        return null;
    }

    public static string? ValidateTaskBody(
        string? title,
        int projectId,
        string? description,
        DateTime? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Title is required.";
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTaskTitleLength)
        {
            return $"Title must be at most {MaxTaskTitleLength} characters.";
        }

        if (projectId <= 0)
        {
            return "ProjectId must be a positive integer.";
        }

        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length > MaxTaskDescriptionLength)
        {
            return $"Description must be at most {MaxTaskDescriptionLength} characters.";
        }

        if (dueDate.HasValue)
        {
            var d = dueDate.Value;
            if (d.Year < 2000 || d.Year > 2100)
            {
                return "Due date year must be between 2000 and 2100.";
            }
        }

        return null;
    }
}

using TaskDashboard.Api.Validation;

namespace TaskDashboard.Api.Tests;

/// <summary>
/// Covers validation rules used by Projects and Tasks APIs (no HTTP layer).
/// </summary>
public class RequestValidatorsTests
{
    [Fact]
    public void ValidateTaskBody_EmptyTitle_ReturnsError()
    {
        var err = RequestValidators.ValidateTaskBody("", 1, null, null);
        Assert.Equal("Title is required.", err);
    }

    [Fact]
    public void ValidateTaskBody_WhitespaceTitle_ReturnsError()
    {
        var err = RequestValidators.ValidateTaskBody("   ", 1, null, null);
        Assert.Equal("Title is required.", err);
    }

    [Fact]
    public void ValidateTaskBody_TitleTooLong_ReturnsError()
    {
        var longTitle = new string('x', RequestValidators.MaxTaskTitleLength + 1);
        var err = RequestValidators.ValidateTaskBody(longTitle, 1, null, null);
        Assert.Contains("at most", err, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTaskBody_InvalidProjectId_ReturnsError()
    {
        var err = RequestValidators.ValidateTaskBody("Valid title", 0, null, null);
        Assert.Equal("ProjectId must be a positive integer.", err);

        err = RequestValidators.ValidateTaskBody("Valid title", -1, null, null);
        Assert.Equal("ProjectId must be a positive integer.", err);
    }

    [Fact]
    public void ValidateTaskBody_MissingDueDate_IsOk()
    {
        var err = RequestValidators.ValidateTaskBody("Buy milk", 1, null, null);
        Assert.Null(err);
    }

    [Fact]
    public void ValidateTaskBody_DueDateYearOutOfRange_ReturnsError()
    {
        var err = RequestValidators.ValidateTaskBody("Task", 1, null, new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("2000", err, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTaskBody_ValidDueDate_IsOk()
    {
        var err = RequestValidators.ValidateTaskBody("Task", 1, null, new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        Assert.Null(err);
    }

    [Fact]
    public void ValidateProjectBody_MissingName_ReturnsError()
    {
        var err = RequestValidators.ValidateProjectBody(null, "Cat", categoryRequired: true);
        Assert.Equal("Name is required.", err);
    }

    [Fact]
    public void ValidateProjectBody_MissingCategoryWhenRequired_ReturnsError()
    {
        var err = RequestValidators.ValidateProjectBody("My Project", "  ", categoryRequired: true);
        Assert.Equal("Category is required.", err);
    }

    [Fact]
    public void ValidateProjectBody_Valid_IsOk()
    {
        var err = RequestValidators.ValidateProjectBody("School", "Education", categoryRequired: true);
        Assert.Null(err);
    }
}

using TaskDashboard.Api.Infrastructure.Data;
using TaskDashboard.Api.Services;
using DotNetEnv;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendClient", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(static origin =>
                {
                    if (string.IsNullOrEmpty(origin)) return false;
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                    return uri.Scheme is "http" or "https"
                        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));
                })
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            var fromConfig = builder.Configuration["Cors:AllowedOrigins"]?
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();
            var defaults = new[] { "http://127.0.0.1:5500", "http://localhost:5500" };
            var origins = defaults.Concat(fromConfig)
                .Where(static o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddSingleton<SqlConnectionFactory>();

builder.Services.AddHttpClient<OpenAiAiService>();
builder.Services.AddScoped<IAiService>(sp => sp.GetRequiredService<OpenAiAiService>());

var app = builder.Build();
await EnsureDatabaseAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("FrontendClient");
app.MapControllers();
// Browsers only hit `/`; the API has no static site — send them to a real endpoint.
app.MapGet("/", () => Results.Redirect("/api/health", permanent: false));

app.Run();

static async Task EnsureDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await using var conn = factory.CreateConnection();
        await conn.OpenAsync();

        const string createProjectsSql = """
            CREATE TABLE IF NOT EXISTS Projects (
              Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              Name VARCHAR(200) NOT NULL,
              Description TEXT NULL,
              Category VARCHAR(100) NOT NULL,
              GoalPurpose TEXT NULL
            );
            """;
        const string hasGoalPurposeSql = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'Projects'
              AND COLUMN_NAME = 'GoalPurpose';
            """;
        const string alterProjectsSql = """
            ALTER TABLE Projects
            ADD COLUMN GoalPurpose TEXT NULL;
            """;
        const string createTasksSql = """
            CREATE TABLE IF NOT EXISTS Tasks (
              Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              Title VARCHAR(500) NOT NULL,
              Description TEXT NULL,
              Priority INT NOT NULL,
              DueDate DATETIME NULL,
              Status INT NOT NULL,
              ProjectId INT NOT NULL,
              CreatedAt DATETIME NOT NULL,
              UpdatedAt DATETIME NOT NULL,
              CONSTRAINT FK_Tasks_Projects FOREIGN KEY (ProjectId) REFERENCES Projects (Id) ON DELETE CASCADE
            );
            """;
        const string alterTasksSql = """
            ALTER TABLE Tasks
            ADD COLUMN UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;
            """;
        const string hasUpdatedAtSql = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'Tasks'
              AND COLUMN_NAME = 'UpdatedAt';
            """;
        const string backfillUpdatedAtSql = """
            UPDATE Tasks
            SET UpdatedAt = CreatedAt;
            """;
        const string seedProjectsSql = """
            INSERT INTO Projects (Id, Name, Description, Category, GoalPurpose)
            SELECT 1, 'School MIS321', 'Group coursework and personal progress tracking.', 'Education', 'Ship core MIS321 group deliverables on time with clear ownership.'
            WHERE NOT EXISTS (SELECT 1 FROM Projects WHERE Id = 1);

            INSERT INTO Projects (Id, Name, Description, Category, GoalPurpose)
            SELECT 2, 'Personal Admin', 'Small ongoing tasks to keep life running smoothly.', 'Personal', 'Keep personal operations predictable and low-stress week to week.'
            WHERE NOT EXISTS (SELECT 1 FROM Projects WHERE Id = 2);
            """;
        const string seedTasksSql = """
            INSERT INTO Tasks (Id, Title, Description, Priority, DueDate, Status, ProjectId, CreatedAt, UpdatedAt)
            SELECT 1, 'Draft project plan', 'Outline goals, milestones, and dashboard structure.', 1, '2026-03-25 17:00:00', 0, 1, '2026-03-23 09:00:00', '2026-03-23 09:00:00'
            WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE Id = 1);

            INSERT INTO Tasks (Id, Title, Description, Priority, DueDate, Status, ProjectId, CreatedAt, UpdatedAt)
            SELECT 2, 'Complete API scaffolding', 'Implement REST API endpoints and database setup.', 2, '2026-03-26 17:00:00', 1, 1, '2026-03-23 09:15:00', '2026-03-23 09:15:00'
            WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE Id = 2);

            INSERT INTO Tasks (Id, Title, Description, Priority, DueDate, Status, ProjectId, CreatedAt, UpdatedAt)
            SELECT 3, 'Set up weekly review', 'Use dashboard to manage personal recurring tasks.', 0, NULL, 0, 2, '2026-03-23 09:30:00', '2026-03-23 09:30:00'
            WHERE NOT EXISTS (SELECT 1 FROM Tasks WHERE Id = 3);
            """;

        await using var createProjects = conn.CreateCommand();
        createProjects.CommandText = createProjectsSql;
        await createProjects.ExecuteNonQueryAsync();

        await using var hasGoalPurpose = conn.CreateCommand();
        hasGoalPurpose.CommandText = hasGoalPurposeSql;
        var goalPurposeExists = Convert.ToInt32(await hasGoalPurpose.ExecuteScalarAsync()) > 0;
        if (!goalPurposeExists)
        {
            await using var alterProjects = conn.CreateCommand();
            alterProjects.CommandText = alterProjectsSql;
            await alterProjects.ExecuteNonQueryAsync();
        }

        await using var createTasks = conn.CreateCommand();
        createTasks.CommandText = createTasksSql;
        await createTasks.ExecuteNonQueryAsync();

        await using var hasUpdatedAt = conn.CreateCommand();
        hasUpdatedAt.CommandText = hasUpdatedAtSql;
        var updatedAtExists = Convert.ToInt32(await hasUpdatedAt.ExecuteScalarAsync()) > 0;
        if (!updatedAtExists)
        {
            await using var alterTasks = conn.CreateCommand();
            alterTasks.CommandText = alterTasksSql;
            await alterTasks.ExecuteNonQueryAsync();

            await using var backfillUpdatedAt = conn.CreateCommand();
            backfillUpdatedAt.CommandText = backfillUpdatedAtSql;
            await backfillUpdatedAt.ExecuteNonQueryAsync();
        }

        await using var seedProjects = conn.CreateCommand();
        seedProjects.CommandText = seedProjectsSql;
        await seedProjects.ExecuteNonQueryAsync();

        await using var seedTasks = conn.CreateCommand();
        seedTasks.CommandText = seedTasksSql;
        await seedTasks.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed.");
        throw;
    }
}

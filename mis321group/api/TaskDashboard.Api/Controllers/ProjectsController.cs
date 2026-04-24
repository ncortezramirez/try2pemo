using Microsoft.AspNetCore.Mvc;
using TaskDashboard.Api.Infrastructure.Data;
using TaskDashboard.Api.Validation;

namespace TaskDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var projects = new List<object>();
        await using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, Description, Category, GoalPurpose
            FROM Projects
            ORDER BY Name;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var descriptionOrdinal = reader.GetOrdinal("Description");
            var goalPurposeOrdinal = reader.GetOrdinal("GoalPurpose");
            projects.Add(new
            {
                Id = reader.GetInt32("Id"),
                Name = reader.GetString("Name"),
                Description = reader.IsDBNull(descriptionOrdinal) ? null : reader.GetString("Description"),
                Category = reader.GetString("Category"),
                GoalPurpose = reader.IsDBNull(goalPurposeOrdinal) ? null : reader.GetString("GoalPurpose")
            });
        }

        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var err = RequestValidators.ValidateProjectBody(request.Name, request.Category, request.GoalPurpose, categoryRequired: true);
        if (err is not null)
        {
            return BadRequest(new { error = err });
        }

        try
        {
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Projects (Name, Description, Category, GoalPurpose)
                VALUES (@name, @description, @category, @goalPurpose);
                SELECT LAST_INSERT_ID();
                """;
            cmd.Parameters.AddWithValue("@name", request.Name!.Trim());
            cmd.Parameters.AddWithValue("@description",
                string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim());
            cmd.Parameters.AddWithValue("@category", request.Category!.Trim());
            cmd.Parameters.AddWithValue("@goalPurpose", request.GoalPurpose!.Trim());

            var newId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return Created($"/api/projects/{newId}", new
            {
                Id = newId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Category = request.Category.Trim(),
                GoalPurpose = request.GoalPurpose.Trim()
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while creating the project." });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProjectRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        var err = RequestValidators.ValidateProjectBody(request.Name, request.Category, request.GoalPurpose, categoryRequired: true);
        if (err is not null)
        {
            return BadRequest(new { error = err });
        }

        try
        {
            await using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Projects
                SET Name = @name, Description = @description, Category = @category, GoalPurpose = @goalPurpose
                WHERE Id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", request.Name!.Trim());
            cmd.Parameters.AddWithValue("@description",
                string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim());
            cmd.Parameters.AddWithValue("@category", request.Category!.Trim());
            cmd.Parameters.AddWithValue("@goalPurpose", request.GoalPurpose!.Trim());

            var changed = await cmd.ExecuteNonQueryAsync();
            if (changed == 0)
            {
                return NotFound(new { error = "Project not found." });
            }

            return Ok(new
            {
                Id = id,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Category = request.Category.Trim(),
                GoalPurpose = request.GoalPurpose.Trim()
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while updating the project." });
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
            cmd.CommandText = "DELETE FROM Projects WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            var changed = await cmd.ExecuteNonQueryAsync();
            if (changed == 0)
            {
                return NotFound(new { error = "Project not found." });
            }
            return Ok(new { message = "Project deleted successfully." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Something went wrong while deleting the project." });
        }
    }

    public class ProjectRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public string GoalPurpose { get; set; } = string.Empty;
    }
}

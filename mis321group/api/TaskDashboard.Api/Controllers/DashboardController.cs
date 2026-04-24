using Microsoft.AspNetCore.Mvc;

namespace TaskDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    // Unified dashboard entrypoint (single-board concept) placeholder.
    [HttpGet]
    public IActionResult Get() => Ok(new { message = "Dashboard endpoint scaffolded." });
}

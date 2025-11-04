using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/project-managers")]
public class ProjectManagersController : ControllerBase
{
    [HttpGet("welcome")]
    public IActionResult Welcome()
    {
        return Ok("Welcome, Project Manager!");
    }
}

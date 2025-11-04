using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/members")]
public class MembersController : ControllerBase
{
    [HttpGet("welcome")]
    public IActionResult Welcome()
    {
        return Ok("Welcome, Member!");
    }
}

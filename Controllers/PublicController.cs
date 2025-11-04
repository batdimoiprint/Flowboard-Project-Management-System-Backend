using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/public")]
public class PublicController : ControllerBase
{
    [HttpGet("welcome")]
    public IActionResult Welcome()
    {
        return Ok("Welcome to the public API view!");
    }
}

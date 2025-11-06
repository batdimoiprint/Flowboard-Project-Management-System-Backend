using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public UsersController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    // Returns the current user using a simple Bearer token convention (Bearer {userId})
    [HttpGet("me")]
    public IActionResult Me()
    {
        var auth = Request.Headers["Authorization"].ToString();
        string? userId = null;
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer "))
        {
            userId = auth.Substring("Bearer ".Length).Trim();
        }

        if (string.IsNullOrWhiteSpace(userId) && Request.Headers.ContainsKey("x-user-id"))
        {
            userId = Request.Headers["x-user-id"].ToString();
        }

        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { message = "Missing Authorization header (expected Bearer {userId})" });

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");
        var user = usersCollection.Find(u => u.Id == userId).FirstOrDefault();
        if (user == null) return NotFound(new { message = "User not found." });

    user.Password = string.Empty;
        return Ok(user);
    }

    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");
        var user = usersCollection.Find(u => u.Id == id).FirstOrDefault();
        if (user == null) return NotFound(new { message = "User not found." });

    user.Password = string.Empty;
        return Ok(user);
    }
}

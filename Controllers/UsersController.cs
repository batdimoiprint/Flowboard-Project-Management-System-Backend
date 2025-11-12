using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

[ApiController]
[Route("api/users")]
[Authorize] // Protect all routes in this controller with JWT
public class UsersController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public UsersController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    // Returns the current user based on JWT
    [HttpGet("me")]
    public IActionResult Me()
    {
        // Extract userId from JWT claims
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new { message = "Invalid token: missing user ID." });

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");
        var user = usersCollection.Find(u => u.Id == userId).FirstOrDefault();
        if (user == null) return NotFound(new { message = "User not found." });

        user.Password = string.Empty;
        return Ok(user);
    }

    // Get a user by ID (still protected)
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

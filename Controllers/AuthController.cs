using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public AuthController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email and password are required." });

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");

        var user = usersCollection.Find(u => u.Email == dto.Email && u.Password == dto.Password).FirstOrDefault();
        if (user == null) return Unauthorized(new { message = "Invalid email or password." });

    // Hide password
    user.Password = string.Empty;

        // Note: no JWT implementation yet; return a simple token placeholder (user id)
        var token = user.Id;

        return Ok(new { message = "Login successful", token, user });
    }

    public class LoginDto
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}

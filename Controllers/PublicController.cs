using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using System;

using BCrypt.Net;
// Route changed to api/auth for registration
[ApiController]
[Route("api/auth")]
public class PublicController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public PublicController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    [HttpGet("welcome")]
    public IActionResult Welcome()
    {
        return Ok("Welcome to the public API view!");
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] User user)
    {
        if (user == null || string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");

        // Check if email already exists
        var existingUser = usersCollection.Find(u => u.Email == user.Email).FirstOrDefault();
        if (existingUser != null)
        {
            return Conflict(new { message = "Email already registered." });
        }

        user.CreatedAt = DateTime.UtcNow;

        user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);

        usersCollection.InsertOne(user);

        user.Password = null;
        return Ok(new { message = "Registration successful!", user });
    }
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest loginRequest)

    {
        if (loginRequest == null || string.IsNullOrEmpty(loginRequest.Email) || string.IsNullOrEmpty(loginRequest.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<User>("user");

        // Find user by email
        var user = usersCollection.Find(u => u.Email == loginRequest.Email).FirstOrDefault();

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        // Verify hashed password
        bool isPasswordValid = BCrypt.Net.BCrypt.Verify(loginRequest.Password, user.Password);

        if (!isPasswordValid)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        // Hide password before sending back
        user.Password = null;

        return Ok(new
        {
            message = "Login successful!",
            user
        });
    }

}

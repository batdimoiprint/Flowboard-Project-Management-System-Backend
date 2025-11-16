using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

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

    [HttpPost("register")]
    public IActionResult Register([FromBody] FlowModels.User user)
    {
        if (user == null || string.IsNullOrEmpty(user.Email) ||
        string.IsNullOrEmpty(user.UserName) ||
        string.IsNullOrEmpty(user.Password))
        {
            return BadRequest(new { message = "Email or Username and password are required." });
        }

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<FlowModels.User>("user");

        // Check if email already exists
        var existingUser = usersCollection.Find(u => u.Email == user.Email).FirstOrDefault();
        if (existingUser != null)
        {
            return Conflict(new { message = "Username or Email already registered." });
        }

        user.CreatedAt = DateTime.UtcNow;
        user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);

        usersCollection.InsertOne(user);

        user.Password = string.Empty;
        return Ok(new { message = "Registration successful!", user });
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] FlowModels.LoginRequest loginRequest)
    {
        if (loginRequest == null || 
            string.IsNullOrWhiteSpace(loginRequest.UserNameOrEmail) || 
            string.IsNullOrWhiteSpace(loginRequest.Password))
        {
            return BadRequest(new { message = "Username or email and password are required." });
        }

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<FlowModels.User>("user");

        var input = loginRequest.UserNameOrEmail.Trim().ToLower();

        // Find user by username OR email (case-insensitive)
        var user = usersCollection.Find(u =>
            u.Email.ToLower() == input || u.UserName.ToLower() == input
        ).FirstOrDefault();

        if (user == null || !BCrypt.Net.BCrypt.Verify(loginRequest.Password, user.Password))
        {
            return Unauthorized(new { message = "Invalid username/email or password." });
        }

        // Hide password before sending back
        user.Password = string.Empty;

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return Ok(new
        {
            message = "Login successful!",
            user,
            token
        });
    }

        // ---------------- JWT Helper ----------------
    private string GenerateJwtToken(FlowModels.User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_KEY")!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")!;
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")!;
        var expiryMinutes = int.Parse(Environment.GetEnvironmentVariable("JWT_EXPIRY_MINUTES") ?? "60");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", "User") // optional role
        };

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

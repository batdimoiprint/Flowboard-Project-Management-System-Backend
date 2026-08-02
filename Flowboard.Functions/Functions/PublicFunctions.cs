using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Flowboard.Functions.Middleware;
using Flowboard_Project_Management_System_Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/PublicController.cs. Original routing:
    ///   [Route("api/auth")]
    ///   [HttpPost("register")]  Register()  -> POST api/auth/register
    ///   [HttpPost("login")]     Login()     -> POST api/auth/login
    ///
    /// Both endpoints are [AllowAnonymous] in the original (no [Authorize] anywhere on the
    /// controller), so both are marked with our own Flowboard.Functions.Middleware.AllowAnonymous
    /// here per HomeFunctions.cs's documented rule.
    /// </summary>
    public class PublicFunctions
    {
        private readonly MongoDbService _mongoDbService;

        public PublicFunctions(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        [Function("Public_Register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/auth/register")] HttpRequest req)
        {
            FlowModels.User? user;
            try
            {
                user = await req.ReadFromJsonAsync<FlowModels.User>();
            }
            catch
            {
                user = null;
            }

            if (user == null || string.IsNullOrEmpty(user.Email) ||
            string.IsNullOrEmpty(user.UserName) ||
            string.IsNullOrEmpty(user.Password))
            {
                return new BadRequestObjectResult(new { message = "Email or Username and password are required." });
            }

            // Validate role if provided
            if (!string.IsNullOrEmpty(user.Role) &&
                user.Role != "Admin" &&
                user.Role != "User" &&
                user.Role != "Client")
            {
                return new BadRequestObjectResult(new { message = "Invalid role. Allowed roles: Admin, User, Client" });
            }

            // Default to 'User' if not specified
            if (string.IsNullOrEmpty(user.Role))
            {
                user.Role = "User";
            }

            var db = _mongoDbService.GetDatabase();
            var usersCollection = db.GetCollection<FlowModels.User>("user");

            // Check if email already exists
            var existingUser = usersCollection.Find(u => u.Email == user.Email).FirstOrDefault();
            if (existingUser != null)
            {
                return new ObjectResult(new { message = "Username or Email already registered." })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }

            user.CreatedAt = DateTime.UtcNow;
            user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);

            usersCollection.InsertOne(user);

            user.Password = string.Empty;
            return new OkObjectResult(new { message = "Registration successful!", user });
        }

        [Function("Public_Login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/auth/login")] HttpRequest req)
        {
            FlowModels.LoginRequest? loginRequest;
            try
            {
                loginRequest = await req.ReadFromJsonAsync<FlowModels.LoginRequest>();
            }
            catch
            {
                loginRequest = null;
            }

            if (loginRequest == null ||
                string.IsNullOrWhiteSpace(loginRequest.UserNameOrEmail) ||
                string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                return new BadRequestObjectResult(new { message = "Username or email and password are required." });
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
                return new UnauthorizedObjectResult(new { message = "Invalid username/email or password." });
            }

            // Hide password before sending back
            user.Password = string.Empty;

            // Generate JWT token
            var token = GenerateJwtToken(user);

            return new OkObjectResult(new
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
                new Claim(ClaimTypes.Role, user.Role ?? "User")
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
}

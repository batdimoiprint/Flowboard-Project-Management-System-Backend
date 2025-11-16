using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using MongoDB.Bson;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

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

    // Helper: Extract user ID from JWT
    private string? GetUserIdFromToken()
    {
        if (User == null) return null;

        var userId =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
            User.FindFirst("id")?.Value ??
            User.FindFirst("userId")?.Value;

        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    // Returns all users (passwords stripped) for assignment dropdowns
    [HttpGet]
    public IActionResult GetAll()
    {
        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<FlowModels.User>("user");
        var users = usersCollection.Find(_ => true).ToList();
        foreach (var u in users)
        {
            u.Password = string.Empty;
        }
        return Ok(users);
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
        var usersCollection = db.GetCollection<FlowModels.User>("user");
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
        var usersCollection = db.GetCollection<FlowModels.User>("user");
        var user = usersCollection.Find(u => u.Id == id).FirstOrDefault();
        if (user == null) return NotFound(new { message = "User not found." });

        user.Password = string.Empty;
        return Ok(user);
    }

    // PATCH /api/users/{id} - Partial update (only provided fields are updated)
    [HttpPatch("{id}")]
    public IActionResult Patch(string id, [FromBody] Dictionary<string, object> updates)
    {
        if (!ObjectId.TryParse(id, out _))
            return BadRequest(new { message = "Invalid user ID format." });

        if (updates == null || updates.Count == 0)
            return BadRequest(new { message = "No updates provided." });

        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<FlowModels.User>("user");
        var existingUser = usersCollection.Find(u => u.Id == id).FirstOrDefault();
        if (existingUser == null) return NotFound(new { message = "User not found." });

        var requesterId = GetUserIdFromToken();
        if (requesterId == null || (requesterId != id && !User.IsInRole("Admin")))
            return Forbid("You do not have permission to update this user.");

        var updateDefs = new List<UpdateDefinition<FlowModels.User>>();

        // email conflict check (done early if present)
        if (updates.TryGetValue("email", out var emailObj) && emailObj != null)
        {
            var emailStr = emailObj.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(emailStr))
            {
                var existing = usersCollection.Find(u => u.Email.ToLower() == emailStr.ToLower()).FirstOrDefault();
                if (existing != null && existing.Id != id)
                    return Conflict(new { message = "Email already in use by another user." });
                updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.Email, emailStr));
            }
        }

        foreach (var kv in updates)
        {
            var key = kv.Key.ToLowerInvariant();
            var value = kv.Value;
            switch (key)
            {
                case "username":
                case "userName":
                case "user_name":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.UserName, value?.ToString()));
                    break;
                case "firstname":
                case "firstName":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.FirstName, value?.ToString()));
                    break;
                case "lastname":
                case "lastName":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.LastName, value?.ToString()));
                    break;
                case "middlename":
                case "middleName":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.MiddleName, value?.ToString()));
                    break;
                case "contactnumber":
                case "contact":
                case "contact_number":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.ContactNumber, value?.ToString()));
                    break;
                case "birthdate":
                case "birth_date":
                    if (DateTime.TryParse(value?.ToString(), out var birthDate))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.BirthDate, birthDate));
                    break;
                case "userimg":
                case "user_img":
                case "user_img_base64":
                    if (value is string base64 && !string.IsNullOrWhiteSpace(base64))
                    {
                        try {
                            var bytes = Convert.FromBase64String(base64);
                            updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.UserIMG, bytes));
                        }
                        catch { /* invalid base64; ignore or respond with BadRequest? For now ignore */ }
                    }
                    break;
                case "password":
                    if (!string.IsNullOrWhiteSpace(value?.ToString()))
                    {
                        var hashed = BCrypt.Net.BCrypt.HashPassword(value?.ToString() ?? string.Empty);
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.Password, hashed));
                    }
                    break;
                // intentionally skip Id and CreatedAt updates
                case "id":
                case "createdat":
                case "created_at":
                    // ignore attempts to update immutable fields
                    break;
            }
        }

        if (updateDefs.Count == 0)
            return BadRequest(new { message = "No valid updatable fields provided." });

        try
        {
            var result = usersCollection.UpdateOne(
                Builders<FlowModels.User>.Filter.Eq("_id", ObjectId.Parse(id)),
                Builders<FlowModels.User>.Update.Combine(updateDefs)
            );

            if (result.MatchedCount == 0)
                return NotFound(new { message = "User not found." });

            var updatedUser = usersCollection.Find(u => u.Id == id).FirstOrDefault();
            if (updatedUser != null) updatedUser.Password = string.Empty;
            return Ok(updatedUser);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to update user.", detail = ex.Message });
        }
    }
}

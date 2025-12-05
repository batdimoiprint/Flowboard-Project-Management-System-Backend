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
using System.Linq;
using System.Text.Json;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

[ApiController]
[Route("api/users")]
[Authorize] // Protect all routes in this controller with JWT
public class UsersController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB max
    private static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

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

    // Helper: Convert byte[] to base64 data URL
    private static string? BytesToDataUrl(byte[]? imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;
        
        // Detect image type from magic bytes
        string mimeType = "image/png"; // default
        if (imageBytes.Length >= 3 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
            mimeType = "image/jpeg";
        else if (imageBytes.Length >= 8 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            mimeType = "image/png";
        else if (imageBytes.Length >= 6 && imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
            mimeType = "image/gif";
        else if (imageBytes.Length >= 4 && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x46)
            mimeType = "image/webp";
        
        return $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
    }

    // Helper: Convert base64 data URL to byte[]
    private static byte[]? DataUrlToBytes(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        
        try
        {
            // Handle data URL format: data:image/png;base64,iVBORw0KGgo...
            if (dataUrl.StartsWith("data:"))
            {
                var commaIndex = dataUrl.IndexOf(',');
                if (commaIndex > 0)
                {
                    dataUrl = dataUrl.Substring(commaIndex + 1);
                }
            }
            
            return Convert.FromBase64String(dataUrl);
        }
        catch
        {
            return null;
        }
    }

    // Helper: Convert User to response DTO with base64 image
    private object UserToResponse(FlowModels.User user)
    {
        return new
        {
            id = user.Id,
            userName = user.UserName,
            firstName = user.FirstName,
            lastName = user.LastName,
            middleName = user.MiddleName,
            contactNumber = user.ContactNumber,
            secondaryContactNumber = user.SecondaryContactNumber,
            birthDate = user.BirthDate,
            email = user.Email,
            address = user.Address != null ? new
            {
                region = user.Address.Region,
                regionCode = user.Address.RegionCode,
                province = user.Address.Province,
                provinceCode = user.Address.ProvinceCode,
                cityMunicipality = user.Address.CityMunicipality,
                cityMunicipalityCode = user.Address.CityMunicipalityCode,
                barangay = user.Address.Barangay,
                barangayCode = user.Address.BarangayCode,
                streetAddress = user.Address.StreetAddress,
                zipCode = user.Address.ZipCode
            } : null,
            secondaryAddress = user.SecondaryAddress != null ? new
            {
                region = user.SecondaryAddress.Region,
                regionCode = user.SecondaryAddress.RegionCode,
                province = user.SecondaryAddress.Province,
                provinceCode = user.SecondaryAddress.ProvinceCode,
                cityMunicipality = user.SecondaryAddress.CityMunicipality,
                cityMunicipalityCode = user.SecondaryAddress.CityMunicipalityCode,
                barangay = user.SecondaryAddress.Barangay,
                barangayCode = user.SecondaryAddress.BarangayCode,
                streetAddress = user.SecondaryAddress.StreetAddress,
                zipCode = user.SecondaryAddress.ZipCode
            } : null,
            userIMG = BytesToDataUrl(user.UserIMG),
            createdAt = user.CreatedAt
        };
    }

    // Helper: Extract string value from object (handles JsonElement)
    private static string? GetStringValue(object? value)
    {
        if (value == null) return null;
        
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.Null ? null : jsonElement.GetString() ?? jsonElement.ToString();
        }
        
        return value.ToString();
    }

    // Helper: Check if value is null (handles JsonElement)
    private static bool IsNullValue(object? value)
    {
        if (value == null) return true;
        
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.Null;
        }
        
        return false;
    }

    // Helper: Parse Address from JsonElement or dictionary
    private static FlowModels.Address? ParseAddressFromValue(object? value)
    {
        if (value == null) return null;

        try
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind != JsonValueKind.Object) return null;

                return new FlowModels.Address
                {
                    Region = jsonElement.TryGetProperty("region", out var region) ? region.GetString() ?? string.Empty : string.Empty,
                    RegionCode = jsonElement.TryGetProperty("regionCode", out var regionCode) ? regionCode.GetString() ?? string.Empty : string.Empty,
                    Province = jsonElement.TryGetProperty("province", out var province) ? province.GetString() ?? string.Empty : string.Empty,
                    ProvinceCode = jsonElement.TryGetProperty("provinceCode", out var provinceCode) ? provinceCode.GetString() ?? string.Empty : string.Empty,
                    CityMunicipality = jsonElement.TryGetProperty("cityMunicipality", out var cityMunicipality) ? cityMunicipality.GetString() ?? string.Empty : string.Empty,
                    CityMunicipalityCode = jsonElement.TryGetProperty("cityMunicipalityCode", out var cityMunicipalityCode) ? cityMunicipalityCode.GetString() ?? string.Empty : string.Empty,
                    Barangay = jsonElement.TryGetProperty("barangay", out var barangay) ? barangay.GetString() ?? string.Empty : string.Empty,
                    BarangayCode = jsonElement.TryGetProperty("barangayCode", out var barangayCode) ? barangayCode.GetString() ?? string.Empty : string.Empty,
                    StreetAddress = jsonElement.TryGetProperty("streetAddress", out var streetAddress) ? streetAddress.GetString() ?? string.Empty : string.Empty,
                    ZipCode = jsonElement.TryGetProperty("zipCode", out var zipCode) ? zipCode.GetString() ?? string.Empty : string.Empty
                };
            }

            // Handle Dictionary<string, object> case
            if (value is Dictionary<string, object> dict)
            {
                return new FlowModels.Address
                {
                    Region = dict.TryGetValue("region", out var region) ? GetStringValue(region) ?? string.Empty : string.Empty,
                    RegionCode = dict.TryGetValue("regionCode", out var regionCode) ? GetStringValue(regionCode) ?? string.Empty : string.Empty,
                    Province = dict.TryGetValue("province", out var province) ? GetStringValue(province) ?? string.Empty : string.Empty,
                    ProvinceCode = dict.TryGetValue("provinceCode", out var provinceCode) ? GetStringValue(provinceCode) ?? string.Empty : string.Empty,
                    CityMunicipality = dict.TryGetValue("cityMunicipality", out var cityMunicipality) ? GetStringValue(cityMunicipality) ?? string.Empty : string.Empty,
                    CityMunicipalityCode = dict.TryGetValue("cityMunicipalityCode", out var cityMunicipalityCode) ? GetStringValue(cityMunicipalityCode) ?? string.Empty : string.Empty,
                    Barangay = dict.TryGetValue("barangay", out var barangay) ? GetStringValue(barangay) ?? string.Empty : string.Empty,
                    BarangayCode = dict.TryGetValue("barangayCode", out var barangayCode) ? GetStringValue(barangayCode) ?? string.Empty : string.Empty,
                    StreetAddress = dict.TryGetValue("streetAddress", out var streetAddress) ? GetStringValue(streetAddress) ?? string.Empty : string.Empty,
                    ZipCode = dict.TryGetValue("zipCode", out var zipCode) ? GetStringValue(zipCode) ?? string.Empty : string.Empty
                };
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Returns all users (passwords stripped) for assignment dropdowns
    [HttpGet]
    public IActionResult GetAll()
    {
        var db = _mongoDbService.GetDatabase();
        var usersCollection = db.GetCollection<FlowModels.User>("user");
        var users = usersCollection.Find(_ => true).ToList();
        
        var response = users.Select(u => UserToResponse(u)).ToList();
        return Ok(response);
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

        return Ok(UserToResponse(user));
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
            var emailStr = GetStringValue(emailObj)?.Trim() ?? string.Empty;
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
            var stringValue = GetStringValue(value);
            
            switch (key)
            {
                case "username":
                case "user_name":
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.UserName, stringValue));
                    break;
                case "firstname":
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.FirstName, stringValue));
                    break;
                case "lastname":
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.LastName, stringValue));
                    break;
                case "middlename":
                    updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.MiddleName, stringValue ?? string.Empty));
                    break;
                case "contactnumber":
                case "contact":
                case "contact_number":
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.ContactNumber, stringValue));
                    break;
                case "birthdate":
                case "birth_date":
                    if (DateTime.TryParse(stringValue, out var birthDate))
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.BirthDate, birthDate));
                    break;
                case "userimg":
                case "user_img":
                case "user_img_base64":
                    if (IsNullValue(value))
                    {
                        // Allow clearing the image
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.UserIMG, (byte[]?)null));
                    }
                    else if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        var bytes = DataUrlToBytes(stringValue);
                        if (bytes != null)
                        {
                            if (bytes.Length > MaxImageSizeBytes)
                            {
                                return BadRequest(new { message = "Image size exceeds 5MB limit." });
                            }
                            updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.UserIMG, bytes));
                        }
                    }
                    break;
                case "password":
                    if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        var hashed = BCrypt.Net.BCrypt.HashPassword(stringValue);
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.Password, hashed));
                    }
                    break;
                case "secondarycontactnumber":
                case "secondary_contact_number":
                    if (IsNullValue(value))
                    {
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.SecondaryContactNumber, (string?)null));
                    }
                    else if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.SecondaryContactNumber, stringValue));
                    }
                    break;
                case "address":
                    if (IsNullValue(value))
                    {
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.Address, (FlowModels.Address?)null));
                    }
                    else
                    {
                        var address = ParseAddressFromValue(value);
                        if (address != null)
                        {
                            updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.Address, address));
                        }
                    }
                    break;
                case "secondaryaddress":
                case "secondary_address":
                    if (IsNullValue(value))
                    {
                        updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.SecondaryAddress, (FlowModels.Address?)null));
                    }
                    else
                    {
                        var secondaryAddress = ParseAddressFromValue(value);
                        if (secondaryAddress != null)
                        {
                            updateDefs.Add(Builders<FlowModels.User>.Update.Set(u => u.SecondaryAddress, secondaryAddress));
                        }
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
            if (updatedUser == null) return NotFound(new { message = "User not found after update." });
            
            return Ok(UserToResponse(updatedUser));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to update user.", detail = ex.Message });
        }
    }
}

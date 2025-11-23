using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using MongoDB.Bson;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/projects")]
    [Authorize] // JWT Protected
    public class ProjectsController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;

        // DTO for adding team members and optional permissions
        public class AddMembersDto
        {
            public List<string>? TeamMembers { get; set; }
            public Dictionary<string, string>? Permissions { get; set; }
            // Support single value usage in case client sends a single string instead of array
            public string? TeamMember { get; set; }
        }
        
        // DTO for removing a single team member
        public class RemoveMemberDto
        {
            // Support two keys: `teamMember` (preferred) or `teamMembers` (compatibility)
            public string? TeamMember { get; set; }
            public string? TeamMembers { get; set; }
        }
        [HttpPost("{id}/member")]
        public IActionResult AddMembers(string id, [FromBody] AddMembersDto dto)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        return BadRequest(new { message = "Invalid id." });
                    if (dto == null || (dto.TeamMembers == null && dto.Permissions == null && string.IsNullOrWhiteSpace(dto.TeamMember)))
                        return BadRequest(new { message = "No team members or permissions provided." });

                    var db = _mongoDbService.GetDatabase();
                    var collection = db.GetCollection<FlowModels.Project>("project");
                    var project = collection.Find(p => p.Id == id).FirstOrDefault();
                    if (project == null)
                        return NotFound(new { message = "Project not found." });

                    var requesterId = GetUserIdFromToken();
                    if (requesterId == null)
                        return Unauthorized(new { message = "Invalid user token." });

                    // Only Owner or Editor or Admin can add members; only Owner (or Admin) can change permissions/roles.
                    var canEditMembers = HasEditPermission(project, requesterId) || User.IsInRole("Admin");
                    if (!canEditMembers)
                        return Forbid("You do not have permission to modify team members.");

                    var updateDefs = new List<UpdateDefinition<FlowModels.Project>>();

                    // Validate that provided user IDs actually exist in users collection
                    var dbUsersCollection = db.GetCollection<FlowModels.User>("user");
                    var candidateIds = new HashSet<string>();
                    if (dto.TeamMembers != null)
                    {
                        foreach (var v in dto.TeamMembers)
                        {
                            if (!string.IsNullOrWhiteSpace(v)) candidateIds.Add(v);
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(dto.TeamMember))
                        candidateIds.Add(dto.TeamMember!);
                    if (dto.Permissions != null)
                    {
                        foreach (var kv in dto.Permissions)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Key)) candidateIds.Add(kv.Key);
                        }
                    }
                    if (candidateIds.Count > 0)
                    {
                        var userFilter = Builders<FlowModels.User>.Filter.In(u => u.Id, candidateIds);
                        var foundUsers = dbUsersCollection.Find(userFilter).Project(u => u.Id).ToList();
                        var foundSet = new HashSet<string>(foundUsers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!));
                        var invalid = candidateIds.Where(idc => !foundSet.Contains(idc)).ToList();
                        if (invalid.Count > 0)
                        {
                            return BadRequest(new { message = "Invalid user IDs provided.", invalidUserIds = invalid });
                        }
                    }

                    // teamMembers: merge into existing list (dedupe)
                    if ((dto.TeamMembers != null && dto.TeamMembers.Count > 0) || !string.IsNullOrWhiteSpace(dto.TeamMember))
                    {
                        var members = project.TeamMembers ?? new List<string>();
                        if (dto.TeamMembers != null)
                        {
                            foreach (var m in dto.TeamMembers)
                            {
                                if (string.IsNullOrWhiteSpace(m)) continue;
                                // ensure added user exists and isn't already on the team
                                if (!members.Contains(m)) members.Add(m);
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(dto.TeamMember))
                        {
                            var single = dto.TeamMember.Trim();
                            if (!members.Contains(single)) members.Add(single);
                        }
                        // ensure creator remains in team
                        if (!string.IsNullOrWhiteSpace(project.CreatedBy) && !members.Contains(project.CreatedBy)) members.Add(project.CreatedBy);
                        updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, members));
                    }

                    // permissions: only Owner or Admin can update roles
                    Dictionary<string, string> newPermissions = project.Permissions != null ? new Dictionary<string, string>(project.Permissions) : new Dictionary<string, string>();
                    if (dto.Permissions != null && dto.Permissions.Count > 0)
                    {
                        var isOwner = project.Permissions != null && project.Permissions.TryGetValue(requesterId, out var role) && role == "Owner";
                        if (!isOwner && !User.IsInRole("Admin"))
                            return Forbid("Only the project owner or an admin can update permissions.");

                        var membersChangedDuringPermissions = false;
                        foreach (var kv in dto.Permissions)
                        {
                            var key = kv.Key;
                            var val = kv.Value;
                            if (string.IsNullOrWhiteSpace(val))
                            {
                                // treat empty as removal
                                if (newPermissions.ContainsKey(key)) newPermissions.Remove(key);
                            }
                            else
                            {
                                newPermissions[key] = val;
                            }
                            // ensure the user is also added to team members
                            if (!string.IsNullOrWhiteSpace(key))
                            {
                                var members = project.TeamMembers ?? new List<string>();
                                if (!members.Contains(key)) members.Add(key);
                                project.TeamMembers = members; // update in-memory to reflect changes for further actions
                                membersChangedDuringPermissions = true;
                            }
                        }
                        updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.Permissions, newPermissions));
                        if (membersChangedDuringPermissions)
                        {
                            updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, project.TeamMembers));
                        }
                    }

                    if (updateDefs.Count == 0)
                        return BadRequest(new { message = "No valid updatable fields provided." });

                    try
                    {
                        var result = collection.UpdateOne(
                            Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                            Builders<FlowModels.Project>.Update.Combine(updateDefs)
                        );
                        if (result.MatchedCount == 0)
                            return NotFound(new { message = "Project not found." });

                        var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                        return Ok(updatedProject);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { message = "Failed to add members to project.", detail = ex.Message });
                    }
                }

                // 🔹 DELETE /api/projects/{id}/member - remove a single member from project
                [HttpDelete("{id}/member")]
                public IActionResult RemoveMember(string id, [FromBody] RemoveMemberDto dto)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        return BadRequest(new { message = "Invalid id." });
                    if (dto == null || (string.IsNullOrWhiteSpace(dto.TeamMember) && string.IsNullOrWhiteSpace(dto.TeamMembers)))
                        return BadRequest(new { message = "teamMember is required in request body (single id string)." });

                    var memberId = !string.IsNullOrWhiteSpace(dto.TeamMember) ? dto.TeamMember!.Trim() : dto.TeamMembers!.Trim();

                    var db = _mongoDbService.GetDatabase();
                    var collection = db.GetCollection<FlowModels.Project>("project");
                    var project = collection.Find(p => p.Id == id).FirstOrDefault();
                    if (project == null)
                        return NotFound(new { message = "Project not found." });

                    var requesterId = GetUserIdFromToken();
                    if (requesterId == null)
                        return Unauthorized(new { message = "Invalid user token." });

                    // Only Owner or Editor or Admin can remove members
                    var canEditMembers = HasEditPermission(project, requesterId) || User.IsInRole("Admin");
                    if (!canEditMembers)
                        return Forbid("You do not have permission to modify team members.");

                    // Prevent removing the project owner or creator
                    if (!string.IsNullOrWhiteSpace(project.CreatedBy) && project.CreatedBy == memberId)
                        return Forbid("Cannot remove the project owner/creator from team members.");

                    // Verify user exists
                    var usersCollection = db.GetCollection<FlowModels.User>("user");
                    var exists = usersCollection.Find(u => u.Id == memberId).Any();
                    if (!exists)
                        return BadRequest(new { message = "User ID does not exist." });

                    // If user is not actually a team member, return NotFound
                    var members = project.TeamMembers ?? new List<string>();
                    if (!members.Contains(memberId))
                        return NotFound(new { message = "User is not a member of this project." });

                    // If this user has Owner role, prevent deletion by non-admin/non-owner
                    if (project.Permissions != null && project.Permissions.TryGetValue(memberId, out var memberRole) && memberRole == "Owner")
                    {
                        var isRequesterOwner = project.Permissions.ContainsKey(requesterId) && project.Permissions[requesterId] == "Owner";
                        if (!isRequesterOwner && !User.IsInRole("Admin"))
                            return Forbid("Only an owner or admin can remove an owner from the team.");
                    }

                    // Remove from members and permissions if present
                    members.RemoveAll(m => m == memberId);
                    var updateDefs = new List<UpdateDefinition<FlowModels.Project>>();
                    updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, members));

                    if (project.Permissions != null && project.Permissions.ContainsKey(memberId))
                    {
                        var newPermissions = new Dictionary<string, string>(project.Permissions);
                        newPermissions.Remove(memberId);
                        updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.Permissions, newPermissions));
                    }

                    try
                    {
                        var result = collection.UpdateOne(
                            Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                            Builders<FlowModels.Project>.Update.Combine(updateDefs)
                        );
                        if (result.MatchedCount == 0)
                            return NotFound(new { message = "Project not found." });

                        var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                        return Ok(updatedProject);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { message = "Failed to remove member from project.", detail = ex.Message });
                    }
                }

                // 🔹 DELETE /api/projects/{id}/leave - remove the current authenticated user from the project
                [HttpDelete("{id}/leave")]
                public IActionResult LeaveProject(string id)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        return BadRequest(new { message = "Invalid id." });

                    var db = _mongoDbService.GetDatabase();
                    var collection = db.GetCollection<FlowModels.Project>("project");
                    var project = collection.Find(p => p.Id == id).FirstOrDefault();
                    if (project == null)
                        return NotFound(new { message = "Project not found." });

                    var requesterId = GetUserIdFromToken();
                    if (requesterId == null)
                        return Unauthorized(new { message = "Invalid user token." });

                    // Prevent the project owner/creator from 'leaving' the project using this route
                    var isOwner = (!string.IsNullOrWhiteSpace(project.CreatedBy) && project.CreatedBy == requesterId) ||
                                  (project.Permissions != null && project.Permissions.ContainsKey(requesterId) && project.Permissions[requesterId] == "Owner");
                    if (isOwner)
                        return Forbid("Project owners cannot leave the project. Transfer ownership or delete the project instead.");

                    var members = project.TeamMembers ?? new List<string>();
                    if (!members.Contains(requesterId))
                        return NotFound(new { message = "You are not a member of this project." });

                    // Remove member and permissions if any
                    members.RemoveAll(m => m == requesterId);
                    var updateDefs = new List<UpdateDefinition<FlowModels.Project>>();
                    updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, members));

                    if (project.Permissions != null && project.Permissions.ContainsKey(requesterId))
                    {
                        var newPermissions = new Dictionary<string, string>(project.Permissions);
                        newPermissions.Remove(requesterId);
                        updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.Permissions, newPermissions));
                    }

                    try
                    {
                        var result = collection.UpdateOne(
                            Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                            Builders<FlowModels.Project>.Update.Combine(updateDefs)
                        );
                        if (result.MatchedCount == 0)
                            return NotFound(new { message = "Project not found." });

                        var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                        return Ok(updatedProject);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new { message = "Failed to leave project.", detail = ex.Message });
                    }
                }
        public ProjectsController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        // 🔹 Helper: Extract user ID from JWT
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

        // 🔹 Helper: Check if user can edit project
        private bool HasEditPermission(FlowModels.Project project, string userId)
        {
            if (project.Permissions == null) return false;
            if (!project.Permissions.TryGetValue(userId, out var role)) return false;
            return role == "Owner" || role == "Editor";
        }

        // 🔹 GET /api/projects
        [HttpGet]
        public IActionResult GetAll()
        {
            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var projects = collection.Find(_ => true).ToList();
            return Ok(projects);
        }

        // 🔹 GET /api/projects/{id}
        // Behavior: if {id} matches a project ID, return that project; otherwise treat {id} as a userId and return projects created by that user
        // Constrain {id} to 24-character ObjectId strings to avoid ambiguous matches with the base GET
        [HttpGet("{id:length(24)}", Name = "GetProjectById")]
        public IActionResult GetById(string id, [FromQuery] bool includeTasks = false)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");

            // First attempt: try to find a project with this id
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project != null)
            {
                if (includeTasks)
                {
                    var tasksCollection = db.GetCollection<FlowModels.Task>("task");
                    var tasks = tasksCollection.Find(t => t.ProjectId == project.Id).ToList();
                    return Ok(new { project = project, tasks = tasks });
                }
                return Ok(project);
            }

            // Not a project id; treat as user id. Enforce requester must be the user or admin
            var requesterId = GetUserIdFromToken();
            if (requesterId == null)
                return Unauthorized(new { message = "Invalid user token." });
            if (requesterId != id && !User.IsInRole("Admin"))
                return Forbid("You do not have permission to view other user's projects.");

            var userProjects = collection.Find(p => p.CreatedBy == id).ToList();
            return Ok(userProjects);
        }

        // 🔹 POST /api/projects
        [HttpPost]
        public IActionResult Create([FromBody] FlowModels.Project project)
        {
            if (project == null)
                return BadRequest(new { message = "Project is required." });
            if (string.IsNullOrWhiteSpace(project.ProjectName))
                return BadRequest(new { message = "ProjectName is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");

            var userId = GetUserIdFromToken();
            if (userId == null)
                return Unauthorized(new { message = "Invalid user token." });

            project.Id = ObjectId.GenerateNewId().ToString();
            project.CreatedAt = DateTime.UtcNow;
            project.CreatedBy = userId; // ✅ assign automatically
            if (project.TeamMembers == null)
                project.TeamMembers = new List<string>();

            if (!project.TeamMembers.Contains(userId))
                project.TeamMembers.Add(userId); // Ensure creator is in team members

            // Assign Owner permissions to creator
            project.Permissions ??= new Dictionary<string, string>();
            project.Permissions[userId] = "Owner";

            collection.InsertOne(project);

            return CreatedAtRoute("GetProjectById", new { id = project.Id }, project);
        }

        // 🔹 PUT /api/projects/{id}
        [HttpPut("{id}")]
        public IActionResult Update(string id, [FromBody] FlowModels.Project updatedProject)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });
            if (updatedProject == null)
                return BadRequest(new { message = "Project is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var existingProject = collection.Find(p => p.Id == id).FirstOrDefault();

            if (existingProject == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null || !HasEditPermission(existingProject, userId))
                return Forbid("You do not have permission to edit this project.");

            updatedProject.Id = id;
            updatedProject.CreatedAt = existingProject.CreatedAt;
            updatedProject.CreatedBy = existingProject.CreatedBy;
            updatedProject.Permissions = existingProject.Permissions;

            var result = collection.ReplaceOne(p => p.Id == id, updatedProject);

            return NoContent();
        }

        // 🔹 DELETE /api/projects/{id}
        [HttpDelete("{id}")]
        public IActionResult Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null)
                return Unauthorized(new { message = "Invalid user token." });

            var isOwner = project.Permissions != null && project.Permissions.ContainsKey(userId) && project.Permissions[userId] == "Owner";
            if (!isOwner && !User.IsInRole("Admin"))
                return Forbid("Only the project owner or an admin can delete this project.");

            collection.DeleteOne(p => p.Id == id);
            return Ok(new { message = "Project deleted successfully.", id = id });
        }

        // 🔹 PATCH /api/projects/{id}/permissions
        [HttpPatch("{id}/permissions")]
        public IActionResult UpdatePermission(string id, [FromBody] Dictionary<string, string> update)
        {
            if (!update.ContainsKey("userId") || !update.ContainsKey("role"))
                return BadRequest(new { message = "userId and role are required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return NotFound(new { message = "Project not found." });

            var userId = GetUserIdFromToken();
            if (userId == null || !project.Permissions.ContainsKey(userId) || project.Permissions[userId] != "Owner")
                return Forbid("Only the project owner can update permissions.");

            project.Permissions[update["userId"]] = update["role"];
            collection.ReplaceOne(p => p.Id == id, project);

            return Ok(new { message = "Permissions updated." });
        }

        // 🔹 PATCH /api/projects/{id} - Partial update (name/description/teamMembers/permissions)
        [HttpPatch("{id}")]
        public IActionResult Patch(string id, [FromBody] Dictionary<string, object> updates)
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "Invalid id." });
            if (updates == null || updates.Count == 0)
                return BadRequest(new { message = "No updates provided." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return NotFound(new { message = "Project not found." });

            var requesterId = GetUserIdFromToken();
            if (requesterId == null)
                return Unauthorized(new { message = "Invalid user token." });

            // Only Owner or Editor (HasEditPermission) can update name/description/teamMembers.
            if (!HasEditPermission(project, requesterId) && !User.IsInRole("Admin"))
                return Forbid("You do not have permission to update this project.");

            var updateDefs = new List<UpdateDefinition<FlowModels.Project>>();

            // projectName
            if (updates.TryGetValue("projectName", out var pn) && pn != null)
            {
                var pnStr = pn.ToString()?.Trim();
                if (!string.IsNullOrEmpty(pnStr))
                    updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.ProjectName, pnStr));
            }

            // description
            if (updates.TryGetValue("description", out var desc) && desc != null)
            {
                var descStr = desc.ToString();
                updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.Description, descStr));
            }

            // teamMembers (array)
            if (updates.TryGetValue("teamMembers", out var tmVal) && tmVal != null)
            {
                var members = new List<string>();
                if (tmVal is IEnumerable<object> objList)
                {
                    foreach (var it in objList)
                    {
                        if (it == null) continue;
                        var s = it.ToString();
                        if (!string.IsNullOrWhiteSpace(s) && !members.Contains(s)) members.Add(s);
                    }
                }
                else if (tmVal is JsonElement je && je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in je.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s) && !members.Contains(s)) members.Add(s);
                        }
                    }
                }
                else if (tmVal is string single)
                {
                    // Accept single string
                    if (!string.IsNullOrWhiteSpace(single)) members.Add(single);
                }

                // Ensure creator is present
                if (!string.IsNullOrWhiteSpace(project.CreatedBy) && !members.Contains(project.CreatedBy)) members.Add(project.CreatedBy);
                updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, members));
            }

            // permissions (only Owner can update permissions in this endpoint; otherwise use PATCH /permissions)
            if (updates.TryGetValue("permissions", out var permsVal) && permsVal != null)
            {
                if (project.Permissions == null)
                    project.Permissions = new Dictionary<string, string>();

                // require owner
                if (!project.Permissions.ContainsKey(requesterId) || project.Permissions[requesterId] != "Owner")
                    return Forbid("Only the project owner can update permissions.");

                var newPermissions = new Dictionary<string, string>(project.Permissions);

                if (permsVal is Dictionary<string, object> dictObj)
                {
                    foreach (var kv in dictObj)
                    {
                        if (kv.Value == null) continue;
                        var role = kv.Value.ToString();
                        if (string.IsNullOrWhiteSpace(role))
                        {
                            // remove permission
                            if (newPermissions.ContainsKey(kv.Key)) newPermissions.Remove(kv.Key);
                        }
                        else
                        {
                            newPermissions[kv.Key] = role;
                        }
                    }
                }
                else if (permsVal is JsonElement jPerm && jPerm.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in jPerm.EnumerateObject())
                    {
                        var role = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                        if (string.IsNullOrWhiteSpace(role))
                            newPermissions.Remove(prop.Name);
                        else
                            newPermissions[prop.Name] = role;
                    }
                }

                updateDefs.Add(Builders<FlowModels.Project>.Update.Set(p => p.Permissions, newPermissions));
            }

            if (updateDefs.Count == 0)
                return BadRequest(new { message = "No valid updatable fields provided." });

            try
            {
                var result = collection.UpdateOne(
                    Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<FlowModels.Project>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return NotFound(new { message = "Project not found." });

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return Ok(updatedProject);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update project.", detail = ex.Message });
            }
        }
    }
}

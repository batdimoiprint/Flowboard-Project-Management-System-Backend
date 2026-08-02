using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using FlowModels = Flowboard_Project_Management_System_Backend.Models.FlowboardModel;
using Flowboard.Functions.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Port of Controllers/ProjectsController.cs (see HomeFunctions.cs for the mapping rules
    /// this follows).
    ///
    /// Original: [Route("api/projects")], class-level [Authorize], one method-level
    /// [Authorize(Policy = "ProjectRead")] (GetById), and exactly one [AllowAnonymous]
    /// (GetMembers, ~line 454 of the original controller:
    ///   "[HttpGet("{id}/members")] [AllowAnonymous] public IActionResult GetMembers(string id)").
    /// Per JwtAuthMiddleware's doc comment, "ProjectRead" is nothing but
    /// RequireAuthenticatedUser() (see Configurations/SecurityConfiguration.cs), so no policy
    /// engine is needed there - default-deny already enforces it. GetMembers is the ONLY
    /// endpoint below carrying Flowboard.Functions.Middleware.[AllowAnonymous]; every other
    /// endpoint is protected by default.
    ///
    /// The original controller file interleaves several methods (AddMembers, RemoveMember,
    /// UpdateMemberPermissions, LeaveProject) with nested DTO class declarations and the
    /// constructor before the rest of the CRUD endpoints. All of that logic is ported here,
    /// method bodies unchanged, just reordered into a conventional
    /// constructor -> endpoints -> DTOs layout; there is no ASP.NET Core requirement that
    /// controller members appear in any particular order, so this reordering is behavior
    /// preserving.
    ///
    /// Role/ownership checks (User.IsInRole("Client"/"Admin"), NameIdentifier/sub/id/userId
    /// claim lookups, HasEditPermission/GetUserIdFromToken helpers) are ported faithfully
    /// against req.HttpContext.User, which JwtAuthMiddleware populates from the validated
    /// token.
    /// </summary>
    public class ProjectsFunctions
    {
        private readonly MongoDbService _mongoDbService;

        public ProjectsFunctions(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
        }

        // POST /api/projects/{id}/member - Add team members and/or update permissions
        [Function("Projects_AddMembers")]
        public async Task<IActionResult> AddMembers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/projects/{id}/member")] HttpRequest req,
            string id)
        {
            var dto = await ReadBodyAsync<AddMembersDto>(req);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });
            if (dto == null || (dto.TeamMembers == null && dto.Permissions == null && string.IsNullOrWhiteSpace(dto.TeamMember)))
                return new BadRequestObjectResult(new { message = "No team members or permissions provided." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);
            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // Only Owner or Editor or Admin can add members; only Owner (or Admin) can change permissions/roles.
            var canEditMembers = HasEditPermission(project, requesterId) || user.IsInRole("Admin");
            if (!canEditMembers)
                return new ObjectResult(new { message = "You do not have permission to modify team members." }) { StatusCode = 403 };

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
                    return new BadRequestObjectResult(new { message = "Invalid user IDs provided.", invalidUserIds = invalid });
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
                if (!isOwner && !user.IsInRole("Admin"))
                    return new ObjectResult(new { message = "Only the project owner or an admin can update permissions." }) { StatusCode = 403 };

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
                return new BadRequestObjectResult(new { message = "No valid updatable fields provided." });

            try
            {
                var result = collection.UpdateOne(
                    Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<FlowModels.Project>.Update.Combine(updateDefs)
                );
                if (result.MatchedCount == 0)
                    return new NotFoundObjectResult(new { message = "Project not found." });

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return new OkObjectResult(updatedProject);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to add members to project.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // DELETE /api/projects/{id}/member - remove a single member from project
        [Function("Projects_RemoveMember")]
        public async Task<IActionResult> RemoveMember(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/projects/{id}/member")] HttpRequest req,
            string id)
        {
            var dto = await ReadBodyAsync<RemoveMemberDto>(req);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });
            if (dto == null || (string.IsNullOrWhiteSpace(dto.TeamMember) && string.IsNullOrWhiteSpace(dto.TeamMembers)))
                return new BadRequestObjectResult(new { message = "teamMember is required in request body (single id string)." });

            var memberId = !string.IsNullOrWhiteSpace(dto.TeamMember) ? dto.TeamMember!.Trim() : dto.TeamMembers!.Trim();

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);
            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // Only Owner or Editor or Admin can remove members
            var canEditMembers = HasEditPermission(project, requesterId) || user.IsInRole("Admin");
            if (!canEditMembers)
                return new ObjectResult(new { message = "You do not have permission to modify team members." }) { StatusCode = 403 };

            // Prevent removing the project owner or creator
            if (!string.IsNullOrWhiteSpace(project.CreatedBy) && project.CreatedBy == memberId)
                return new ObjectResult(new { message = "Cannot remove the project owner/creator from team members." }) { StatusCode = 403 };

            // Verify user exists
            var usersCollection = db.GetCollection<FlowModels.User>("user");
            var exists = usersCollection.Find(u => u.Id == memberId).Any();
            if (!exists)
                return new BadRequestObjectResult(new { message = "User ID does not exist." });

            // If user is not actually a team member, return NotFound
            var members = project.TeamMembers ?? new List<string>();
            if (!members.Contains(memberId))
                return new NotFoundObjectResult(new { message = "User is not a member of this project." });

            // If this user has Owner role, prevent deletion by non-admin/non-owner
            if (project.Permissions != null && project.Permissions.TryGetValue(memberId, out var memberRole) && memberRole == "Owner")
            {
                var isRequesterOwner = project.Permissions.ContainsKey(requesterId) && project.Permissions[requesterId] == "Owner";
                if (!isRequesterOwner && !user.IsInRole("Admin"))
                    return new ObjectResult(new { message = "Only an owner or admin can remove an owner from the team." }) { StatusCode = 403 };
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
                    return new NotFoundObjectResult(new { message = "Project not found." });

                // Remove the member from all subtasks in this project
                var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
                var subTaskFilter = Builders<FlowModels.SubTask>.Filter.And(
                    Builders<FlowModels.SubTask>.Filter.Eq(st => st.ProjectId, id),
                    Builders<FlowModels.SubTask>.Filter.AnyEq(st => st.AssignedTo, memberId)
                );
                var subTaskUpdate = Builders<FlowModels.SubTask>.Update.Pull(st => st.AssignedTo, memberId);
                var subTasksUpdateResult = subTasksCollection.UpdateMany(subTaskFilter, subTaskUpdate);

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return new OkObjectResult(new
                {
                    project = updatedProject,
                    tasksUpdated = subTasksUpdateResult.ModifiedCount
                });
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to remove member from project.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // PUT /api/projects/{id}/member/{userId}/permissions - update a specific member's role/permissions
        [Function("Projects_UpdateMemberPermissions")]
        public async Task<IActionResult> UpdateMemberPermissions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/projects/{id}/member/{userId}/permissions")] HttpRequest req,
            string id, string userId)
        {
            var dto = await ReadBodyAsync<UpdateMemberPermissionsDto>(req);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid project id." });
            if (string.IsNullOrWhiteSpace(userId))
                return new BadRequestObjectResult(new { message = "Invalid user id." });
            if (dto == null || string.IsNullOrWhiteSpace(dto.Role))
                return new BadRequestObjectResult(new { message = "Role is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);
            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // Only Owner or Admin can update permissions
            var isOwner = project.Permissions != null && project.Permissions.TryGetValue(requesterId, out var requesterRole) && requesterRole == "Owner";
            if (!isOwner && !user.IsInRole("Admin"))
                return new ObjectResult(new { message = "Only the project owner or an admin can update member permissions." }) { StatusCode = 403 };

            // Verify the user exists
            var usersCollection = db.GetCollection<FlowModels.User>("user");
            var userExists = usersCollection.Find(u => u.Id == userId).Any();
            if (!userExists)
                return new BadRequestObjectResult(new { message = "User does not exist." });

            // Ensure user is a team member first
            var members = project.TeamMembers ?? new List<string>();
            if (!members.Contains(userId))
            {
                // Add them to team members if not already
                members.Add(userId);
            }

            // Update permissions dictionary
            var permissions = project.Permissions ?? new Dictionary<string, string>();
            permissions[userId] = dto.Role;

            var updateDefs = new List<UpdateDefinition<FlowModels.Project>>
            {
                Builders<FlowModels.Project>.Update.Set(p => p.TeamMembers, members),
                Builders<FlowModels.Project>.Update.Set(p => p.Permissions, permissions)
            };

            try
            {
                var result = collection.UpdateOne(
                    Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<FlowModels.Project>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return new NotFoundObjectResult(new { message = "Project not found." });

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return new OkObjectResult(updatedProject);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to update member permissions.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // DELETE /api/projects/{id}/leave - remove the current authenticated user from the project
        [Function("Projects_LeaveProject")]
        public IActionResult LeaveProject(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/projects/{id}/leave")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);
            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // Prevent the project owner/creator from 'leaving' the project using this route
            var isOwner = (!string.IsNullOrWhiteSpace(project.CreatedBy) && project.CreatedBy == requesterId) ||
                          (project.Permissions != null && project.Permissions.ContainsKey(requesterId) && project.Permissions[requesterId] == "Owner");
            if (isOwner)
                return new ObjectResult(new { message = "Project owners cannot leave the project. Transfer ownership or delete the project instead." }) { StatusCode = 403 };

            var members = project.TeamMembers ?? new List<string>();
            if (!members.Contains(requesterId))
                return new NotFoundObjectResult(new { message = "You are not a member of this project." });

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
                    return new NotFoundObjectResult(new { message = "Project not found." });

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return new OkObjectResult(updatedProject);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to leave project.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // GET /api/projects
        [Function("Projects_GetAll")]
        public IActionResult GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/projects")] HttpRequest req)
        {
            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var projects = collection.Find(_ => true).ToList();
            return new OkObjectResult(projects);
        }

        // GET /api/projects/member/all - Get projects where the current user is a team member
        [Function("Projects_GetProjectsAsMember")]
        public IActionResult GetProjectsAsMember(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/projects/member/all")] HttpRequest req)
        {
            var userId = GetUserIdFromToken(req.HttpContext.User);
            if (userId == null)
                return new UnauthorizedObjectResult(new { message = "User not authenticated." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");

            // Find projects where the current user is in the teamMembers list
            var projects = collection.Find(p => p.TeamMembers.Contains(userId)).ToList();
            return new OkObjectResult(projects ?? new List<FlowModels.Project>());
        }

        // GET /api/projects/{id}/members - Get all members with their details (must be before {id:length(24)} route)
        // Original carries [AllowAnonymous] - this is the ONE anonymous endpoint in this file.
        [Function("Projects_GetMembers")]
        [AllowAnonymous]
        public IActionResult GetMembers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/projects/{id}/members")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            // Get user details for all team members
            var usersCollection = db.GetCollection<FlowModels.User>("user");
            var memberIds = project.TeamMembers ?? new List<string>();

            // Filter out members who have "Client" role in permissions
            if (project.Permissions != null)
            {
                memberIds = memberIds.Where(memberId =>
                    !project.Permissions.TryGetValue(memberId, out var role) || role != "Client"
                ).ToList();
            }

            if (memberIds.Count == 0)
                return new OkObjectResult(new List<object>());

            var userFilter = Builders<FlowModels.User>.Filter.In(u => u.Id, memberIds);
            var users = usersCollection.Find(userFilter).ToList();

            // Map users with their roles/permissions
            var membersWithRoles = users.Select(u => new
            {
                id = u.Id,
                userName = u.UserName,
                firstName = u.FirstName,
                middleName = u.MiddleName,
                lastName = u.LastName,
                email = u.Email,
                userIMG = u.UserIMG,
                role = project?.Permissions != null && u?.Id != null && project.Permissions.ContainsKey(u.Id)
                    ? project.Permissions[u.Id]
                    : "Team Member"
            }).ToList();

            return new OkObjectResult(membersWithRoles);
        }

        // GET /api/projects/{id}
        // Behavior: if {id} matches a project ID, return that project; otherwise treat {id} as a userId and return projects created by that user
        // Constrain {id} to 24-character ObjectId strings to avoid ambiguous matches with the base GET
        // Clients can only read projects they are assigned to in Permissions
        [Function("Projects_GetById")]
        public IActionResult GetById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/projects/{id:length(24)}")] HttpRequest req,
            string id)
        {
            bool includeTasks = false;
            bool.TryParse(req.Query["includeTasks"], out includeTasks);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);

            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // First attempt: try to find a project with this id
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project != null)
            {
                // Check if user is Client role and verify they have permission on this project
                if (user.IsInRole("Client"))
                {
                    var hasPermission = project.Permissions != null && project.Permissions.ContainsKey(requesterId);
                    if (!hasPermission)
                        return new ObjectResult(new { message = "You do not have permission to access this project." }) { StatusCode = 403 };
                }

                if (includeTasks)
                {
                    var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
                    var tasks = subTasksCollection.Find(t => t.ProjectId == project.Id).ToList();
                    return new OkObjectResult(new { project = project, tasks = tasks });
                }
                return new OkObjectResult(project);
            }

            // Not a project id; treat as user id. Enforce requester must be the user or admin
            if (requesterId != id && !user.IsInRole("Admin"))
                return new ObjectResult(new { message = "You do not have permission to view other user's projects." }) { StatusCode = 403 };

            var userProjects = collection.Find(p => p.CreatedBy == id).ToList();
            return new OkObjectResult(userProjects);
        }

        // POST /api/projects
        [Function("Projects_Create")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/projects")] HttpRequest req)
        {
            var project = await ReadBodyAsync<FlowModels.Project>(req);

            if (project == null)
                return new BadRequestObjectResult(new { message = "Project is required." });
            if (string.IsNullOrWhiteSpace(project.ProjectName))
                return new BadRequestObjectResult(new { message = "ProjectName is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");

            var userId = GetUserIdFromToken(req.HttpContext.User);
            if (userId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            project.Id = ObjectId.GenerateNewId().ToString();
            project.CreatedAt = DateTime.UtcNow;
            project.CreatedBy = userId; // assign automatically
            if (project.TeamMembers == null)
                project.TeamMembers = new List<string>();

            if (!project.TeamMembers.Contains(userId))
                project.TeamMembers.Add(userId); // Ensure creator is in team members

            // Assign Owner permissions to creator
            project.Permissions ??= new Dictionary<string, string>();
            project.Permissions[userId] = "Owner";

            collection.InsertOne(project);

            // Original used CreatedAtRoute("GetProjectById", ...). There is no route-name
            // registry in the isolated worker, so this reproduces the same 201 + body
            // semantics directly against the known route template (api/projects/{id:length(24)}).
            return new ObjectResult(project) { StatusCode = 201 };
        }

        // PUT /api/projects/{id}
        [Function("Projects_Update")]
        public async Task<IActionResult> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/projects/{id}")] HttpRequest req,
            string id)
        {
            var updatedProject = await ReadBodyAsync<FlowModels.Project>(req);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });
            if (updatedProject == null)
                return new BadRequestObjectResult(new { message = "Project is required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var existingProject = collection.Find(p => p.Id == id).FirstOrDefault();

            if (existingProject == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var userId = GetUserIdFromToken(req.HttpContext.User);
            if (userId == null || !HasEditPermission(existingProject, userId))
                return new ObjectResult(new { message = "You do not have permission to edit this project." }) { StatusCode = 403 };

            updatedProject.Id = id;
            updatedProject.CreatedAt = existingProject.CreatedAt;
            updatedProject.CreatedBy = existingProject.CreatedBy;
            updatedProject.Permissions = existingProject.Permissions;

            collection.ReplaceOne(p => p.Id == id, updatedProject);

            return new NoContentResult();
        }

        // DELETE /api/projects/{id}
        [Function("Projects_Delete")]
        public IActionResult Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/projects/{id}")] HttpRequest req,
            string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var userId = GetUserIdFromToken(user);
            if (userId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            var isOwner = project.Permissions != null && project.Permissions.ContainsKey(userId) && project.Permissions[userId] == "Owner";
            if (!isOwner && !user.IsInRole("Admin"))
                return new ObjectResult(new { message = "Only the project owner or an admin can delete this project." }) { StatusCode = 403 };

            // Delete all subtasks associated with this project
            var subTasksCollection = db.GetCollection<FlowModels.SubTask>("subtasks");
            var subTasksDeleteResult = subTasksCollection.DeleteMany(st => st.ProjectId == id);

            // Delete all main tasks associated with this project
            var mainTasksCollection = db.GetCollection<FlowModels.MainTask>("maintasks");
            var mainTasksDeleteResult = mainTasksCollection.DeleteMany(mt => mt.ProjectId == id);

            // Delete all categories associated with this project
            var categoriesCollection = db.GetCollection<FlowModels.Category>("categories");
            var categoriesDeleteResult = categoriesCollection.DeleteMany(c => c.ProjectId == id);

            // Delete the project itself
            collection.DeleteOne(p => p.Id == id);

            return new OkObjectResult(new
            {
                message = "Project and all associated data deleted successfully.",
                id = id,
                deletedSubTasks = subTasksDeleteResult.DeletedCount,
                deletedMainTasks = mainTasksDeleteResult.DeletedCount,
                deletedCategories = categoriesDeleteResult.DeletedCount
            });
        }

        // PATCH /api/projects/{id}/permissions
        [Function("Projects_UpdatePermission")]
        public async Task<IActionResult> UpdatePermission(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/projects/{id}/permissions")] HttpRequest req,
            string id)
        {
            var update = await ReadBodyAsync<Dictionary<string, string>>(req);

            if (update == null || !update.ContainsKey("userId") || !update.ContainsKey("role"))
                return new BadRequestObjectResult(new { message = "userId and role are required." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();

            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var userId = GetUserIdFromToken(req.HttpContext.User);
            if (userId == null || project.Permissions == null || !project.Permissions.ContainsKey(userId) || project.Permissions[userId] != "Owner")
                return new ObjectResult(new { message = "Only the project owner can update permissions." }) { StatusCode = 403 };

            project.Permissions[update["userId"]] = update["role"];
            collection.ReplaceOne(p => p.Id == id, project);

            return new OkObjectResult(new { message = "Permissions updated." });
        }

        // PATCH /api/projects/{id} - Partial update (name/description/teamMembers/permissions)
        [Function("Projects_Patch")]
        public async Task<IActionResult> Patch(
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "api/projects/{id}")] HttpRequest req,
            string id)
        {
            var updates = await ReadBodyAsync<Dictionary<string, object>>(req);

            if (string.IsNullOrWhiteSpace(id))
                return new BadRequestObjectResult(new { message = "Invalid id." });
            if (updates == null || updates.Count == 0)
                return new BadRequestObjectResult(new { message = "No updates provided." });

            var db = _mongoDbService.GetDatabase();
            var collection = db.GetCollection<FlowModels.Project>("project");
            var project = collection.Find(p => p.Id == id).FirstOrDefault();
            if (project == null)
                return new NotFoundObjectResult(new { message = "Project not found." });

            var user = req.HttpContext.User;
            var requesterId = GetUserIdFromToken(user);
            if (requesterId == null)
                return new UnauthorizedObjectResult(new { message = "Invalid user token." });

            // Only Owner or Editor (HasEditPermission) can update name/description/teamMembers.
            if (!HasEditPermission(project, requesterId) && !user.IsInRole("Admin"))
                return new ObjectResult(new { message = "You do not have permission to update this project." }) { StatusCode = 403 };

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
                    return new ObjectResult(new { message = "Only the project owner can update permissions." }) { StatusCode = 403 };

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
                return new BadRequestObjectResult(new { message = "No valid updatable fields provided." });

            try
            {
                var result = collection.UpdateOne(
                    Builders<FlowModels.Project>.Filter.Eq("_id", ObjectId.Parse(id)),
                    Builders<FlowModels.Project>.Update.Combine(updateDefs)
                );

                if (result.MatchedCount == 0)
                    return new NotFoundObjectResult(new { message = "Project not found." });

                var updatedProject = collection.Find(p => p.Id == id).FirstOrDefault();
                return new OkObjectResult(updatedProject);
            }
            catch (Exception ex)
            {
                return new ObjectResult(new { message = "Failed to update project.", detail = ex.Message }) { StatusCode = 500 };
            }
        }

        // Helper: Extract user ID from JWT
        private static string? GetUserIdFromToken(ClaimsPrincipal? user)
        {
            if (user == null) return null;

            var userId =
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                user.FindFirst("id")?.Value ??
                user.FindFirst("userId")?.Value;

            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        // Helper: Check if user can edit project
        private static bool HasEditPermission(FlowModels.Project project, string userId)
        {
            if (project.Permissions == null) return false;
            if (!project.Permissions.TryGetValue(userId, out var role)) return false;
            return role == "Owner" || role == "Editor";
        }

        private static async Task<T?> ReadBodyAsync<T>(HttpRequest req) where T : class
        {
            try
            {
                return await req.ReadFromJsonAsync<T>();
            }
            catch
            {
                return null;
            }
        }

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

        public class UpdateMemberPermissionsDto
        {
            public string? Role { get; set; }
        }
    }
}

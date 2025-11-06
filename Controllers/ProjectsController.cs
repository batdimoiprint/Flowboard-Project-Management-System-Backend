using Microsoft.AspNetCore.Mvc;
using Flowboard_Project_Management_System_Backend.Models;
using Flowboard_Project_Management_System_Backend.Services;
using MongoDB.Driver;
using System;
using System.Collections.Generic;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;

    public ProjectsController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var db = _mongoDbService.GetDatabase();
        var collection = db.GetCollection<Project>("project");
        var projects = collection.Find(_ => true).ToList();
        return Ok(projects);
    }

    [HttpGet("{id}", Name = "GetProjectById")]
    public IActionResult GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });

        var db = _mongoDbService.GetDatabase();
        var collection = db.GetCollection<Project>("project");
        var project = collection.Find(p => p.Id == id).FirstOrDefault();

        if (project == null) return NotFound(new { message = "Project not found." });
        return Ok(project);
    }

    [HttpPost]
    public IActionResult Create([FromBody] Project project)
    {
        if (project == null) return BadRequest(new { message = "Project is required." });
        if (string.IsNullOrWhiteSpace(project.ProjectName)) return BadRequest(new { message = "ProjectName is required." });

        var db = _mongoDbService.GetDatabase();
        var collection = db.GetCollection<Project>("project");

        project.CreatedAt = DateTime.UtcNow;
        if (project.TeamMembers == null) project.TeamMembers = new List<string>();

        collection.InsertOne(project);

        return CreatedAtRoute("GetProjectById", new { id = project.Id }, project);
    }

    [HttpPut("{id}")]
    public IActionResult Update(string id, [FromBody] Project updatedProject)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });
        if (updatedProject == null) return BadRequest(new { message = "Project is required." });

        var db = _mongoDbService.GetDatabase();
        var collection = db.GetCollection<Project>("project");

        updatedProject.Id = id;
        var result = collection.ReplaceOne(p => p.Id == id, updatedProject);

        if (result.MatchedCount == 0) return NotFound(new { message = "Project not found." });

        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Invalid id." });

        var db = _mongoDbService.GetDatabase();
        var collection = db.GetCollection<Project>("project");

        var result = collection.DeleteOne(p => p.Id == id);

        if (result.DeletedCount == 0) return NotFound(new { message = "Project not found." });

        return NoContent();
    }
}

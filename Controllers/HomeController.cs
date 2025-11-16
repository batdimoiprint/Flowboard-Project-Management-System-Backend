using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Flowboard_Project_Management_System_Backend.Services;
[ApiController]
[Route("")]
public class HomeController : ControllerBase


{
 private readonly MongoDbService _mongoDbService;

    public HomeController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }
    [HttpGet]
    public IActionResult Index()
    {
        return Ok("Di ka ba kakarmahin?");
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        try
        {
            var db = _mongoDbService.GetDatabase();
            var result = db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            var okValue = result.Contains("ok") ? result["ok"].ToDouble() : 0.0;
            return Ok(new { message = "Pinged your deployment. You successfully connected to MongoDB!", ok = okValue });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to connect to MongoDB.", error = ex.Message });
        }
    }
}
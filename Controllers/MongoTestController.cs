using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Flowboard_Project_Management_System_Backend.Services;

namespace Flowboard_Project_Management_System_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MongoTestController : ControllerBase
    {
        private readonly MongoDbService _mongoDbService;

        public MongoTestController(MongoDbService mongoDbService)
        {
            _mongoDbService = mongoDbService;
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
}

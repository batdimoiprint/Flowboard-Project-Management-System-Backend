using System;
using System.Threading.Tasks;
using Flowboard.Functions.Middleware;
using Flowboard_Project_Management_System_Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Flowboard.Functions.Functions
{
    /// <summary>
    /// Reference port of Controllers/HomeController.cs - the template the next agent should
    /// copy for the remaining 48 endpoints.
    ///
    /// Original ASP.NET Core routing:
    ///   [Route("")]
    ///   [HttpGet]              Index()  -> GET  /
    ///   [HttpGet("ping")]      Ping()   -> GET  /ping
    ///
    /// Mapping rules demonstrated here:
    ///   - [Route] + [HttpGet]/[HttpPost]/etc.  becomes  [Function("Name")] + [HttpTrigger(..., Route = "...")]
    ///   - host.json sets extensions.http.routePrefix = "" (see Flowboard.Functions/host.json),
    ///     so there is NO implicit "api/" prefix added by the runtime. The Route string here
    ///     must reproduce the ORIGINAL controller path exactly:
    ///       * HomeController has no "api/" segment in its own routes, so these Route strings
    ///         don't have one either.
    ///       * Every other controller in this app is [Route("api/xyz")], so when those are
    ///         ported, their Route strings must start with "api/..." to preserve parity
    ///         (e.g. Route = "api/projects/{id}").
    ///   - [AllowAnonymous] (ASP.NET Core) becomes Flowboard.Functions.Middleware.[AllowAnonymous]
    ///     (our own attribute - JwtAuthMiddleware is default-deny, so an endpoint must opt out
    ///     explicitly to skip auth). Both endpoints below are anonymous, matching the original
    ///     HomeController which has no [Authorize] anywhere.
    ///
    /// HARD RULE - do NOT declare "options" on any HttpTrigger, and do NOT reintroduce CORS
    /// middleware. CORS is configured at the PLATFORM level on the Function App
    /// (`az functionapp cors show -g ProjX-MVC -n func-flowboard-backend`), not in this code.
    ///
    /// Two findings forced this, both verified against the deployed app:
    ///   1. The Functions host intercepts OPTIONS and answers it itself before the worker runs.
    ///      A catch-all preflight function was deployed and NEVER appeared in Application
    ///      Insights telemetry, while every GET and 401 did. Worker-level preflight handling is
    ///      therefore impossible here - middleware cannot see the request to add headers to it.
    ///   2. Declaring "options" per-function collides anyway. The host's route table cannot hold
    ///      duplicate (route, method) pairs, and many endpoints share a route across verbs
    ///      (GET/PUT/DELETE api/subtasks/{id}, GET/POST api/projects, 8 more). That produced 10
    ///      colliding (route, OPTIONS) pairs. Colliding functions fail to register, and the
    ///      symptom is a clean deploy with green CI and routes that 404 at runtime while looking
    ///      correct in source.
    ///
    /// Consequence: adding a new allowed origin is an Azure config change, not a code change.
    /// Adding ACAO headers in code again would emit DUPLICATE Access-Control-Allow-Origin
    /// headers alongside the platform's, which browsers reject outright - worse than no CORS.
    ///
    /// IMPORTANT - programming model: Program.cs calls ConfigureFunctionsWebApplication(), the
    /// ASP.NET Core integration model. Every HTTP function in this app MUST bind
    /// Microsoft.AspNetCore.Http.HttpRequest (not the classic HttpRequestData) and return
    /// Microsoft.AspNetCore.Mvc.IActionResult (not HttpResponseData). Mixing the two binding
    /// models is unsupported by the worker, and - more concretely for this app - the auth/
    /// rate-limit middleware operate on context.GetHttpContext().Response; only the
    /// HttpRequest/IActionResult model shares that same HttpContext end-to-end, so headers set
    /// by middleware are guaranteed to reach the client. Do not introduce HttpRequestData in
    /// any ported endpoint.
    /// </summary>
    public class HomeFunctions
    {
        private readonly MongoDbService _mongoDbService;
        private readonly ILogger<HomeFunctions> _logger;

        public HomeFunctions(MongoDbService mongoDbService, ILogger<HomeFunctions> logger)
        {
            _mongoDbService = mongoDbService;
            _logger = logger;
        }

        [Function("Home_Index")]
        [AllowAnonymous]
        public IActionResult Index(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "")] HttpRequest req)
        {
            return new ContentResult
            {
                Content = "Di ka ba kakarmahin?",
                ContentType = "text/plain; charset=utf-8",
                StatusCode = StatusCodes.Status200OK
            };
        }

        [Function("Home_Ping")]
        [AllowAnonymous]
        public async Task<IActionResult> Ping(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")] HttpRequest req)
        {
            try
            {
                var db = _mongoDbService.GetDatabase();
                var result = await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                var okValue = result.Contains("ok") ? result["ok"].ToDouble() : 0.0;

                return new OkObjectResult(new
                {
                    message = "Pinged your deployment. You successfully connected to MongoDB!",
                    ok = okValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MongoDB.");
                return new ObjectResult(new
                {
                    message = "Failed to connect to MongoDB.",
                    error = ex.Message
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }
    }
}

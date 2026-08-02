using System.Text.Json;
using Azure.Core.Serialization;
using Flowboard.Functions.Middleware;
using Flowboard_Project_Management_System_Backend.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Middleware pipeline order matters:
//   1. CorsMiddleware      - must run before everything else: OPTIONS preflight carries no
//                          Authorization header, so if auth ran first every preflight would
//                          401 and the browser would report it as a CORS failure instead of
//                          the real status. CORS also needs to be able to attach headers to
//                          the final response regardless of what happens downstream.
//   2. RateLimitMiddleware - runs before auth deliberately: an IP flood must be counted and
//                          throttled whether or not the caller has a valid token. If this ran
//                          after JwtAuth, unauthenticated floods would short-circuit at the
//                          401 and never be counted, so the limiter would only ever throttle
//                          legitimate authenticated traffic - defeating its purpose.
//   3. JwtAuthMiddleware   - default-deny bearer token check for everything not marked
//                          [AllowAnonymous]. Runs last so CORS headers and rate-limit
//                          bookkeeping are already applied before we decide auth outcome.
builder.UseMiddleware<RateLimitMiddleware>();
builder.UseMiddleware<JwtAuthMiddleware>();

// JSON must stay camelCase end-to-end - this mirrors the AddJsonOptions() configuration in
// the ASP.NET Core Program.cs. If this is dropped, every API response comes back PascalCase
// (or, for Dictionary<string,int> properties, keeps its original casing) and the frontend
// silently renders empty (it reads camelCase property names and keys).
//
// Two separate knobs are required because functions in this app return
// Microsoft.AspNetCore.Mvc.IActionResult (per the programming-model note in HomeFunctions.cs),
// not HttpResponseData:
//   - WorkerOptions.Serializer   still governs the worker's own binding pipeline (e.g. request
//     body -> POCO conversion for [FromBody] parameters on future POST/PUT endpoints).
//   - Microsoft.AspNetCore.Mvc.JsonOptions governs the MVC IActionResultExecutor that actually
//     serializes OkObjectResult/ObjectResult bodies. Its default (JsonSerializerDefaults.Web)
//     already camelCases property names but does NOT camelCase dictionary keys - and DTOs like
//     SummaryDto.TasksByStatus / TaskProgressDto.StatusBreakdown are Dictionary<string,int>,
//     so this must be set explicitly or those keys silently diverge from what the legacy app
//     (and the frontend) expect.
var jsonSerializerOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
};

builder.Services.Configure<WorkerOptions>(options =>
{
    options.Serializer = new JsonObjectSerializer(jsonSerializerOptions);
});

builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

builder.Build().Run();

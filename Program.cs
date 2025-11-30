// Imports
using Flowboard_Project_Management_System_Backend.Services;
using Flowboard_Project_Management_System_Backend.Configurations;
using DotNetEnv;
using System.Text.Json;

// Builder
var builder = WebApplication.CreateBuilder(args);
Env.Load();
builder.Services.AddFrontendCors(builder.Environment);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use camelCase for JSON property names to match frontend expectations
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddOpenApi();
builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddJwtAuthentication();
var app = builder.Build();


// For Open API
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();

// ---------------- Enable Authentication ----------------
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

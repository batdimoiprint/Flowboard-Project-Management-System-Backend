// Imports
using Flowboard_Project_Management_System_Backend.Services;
using Flowboard_Project_Management_System_Backend.Configurations;
using DotNetEnv;

// Builder
var builder = WebApplication.CreateBuilder(args);
Env.Load();
builder.Services.AddFrontendCors(builder.Environment);
builder.Services.AddControllers();
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

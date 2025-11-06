using Flowboard_Project_Management_System_Backend.Services;
using DotNetEnv; // Add this using statement
// ...other using statements...

Env.Load(); // Load .env variables here


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


// Configure CORS to allow frontend origin
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy
            .WithOrigins("https://flowboard.azurewebsites.net")
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register MongoDbService for dependency injection
builder.Services.AddSingleton<MongoDbService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


// Use CORS policy
app.UseCors("AllowFrontend");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

# Flowboard Backend (ASP.NET Core 9) ✅

## Overview
A simple REST API backend for Flowboard — a project management system built with ASP.NET Core 9 and MongoDB. It provides authentication (JWT), user/project/task management, analytics, and an OpenAPI interface for development.

## Quick Start 🔧
1. Copy or create a `.env` file in the backend folder (an example `.env` is already included).
2. Ensure you have the .NET 9 SDK installed.
3. Run the API:

```bash
cd Flowboard-Project-Management-System-Backend
dotnet restore
dotnet run
```

Or use the workspace task: `Run All` or the `Backend: dev` task.

The app reads environment variables from `.env` (via DotNetEnv). HTTPS port and other settings can be set there (the repository example uses `ASPNETCORE_HTTPS_PORT=7076`).


## API Documentation & Examples
See `Apis.http` in this folder for example API requests and documentation. You can use this file with VS Code REST Client or similar tools to test endpoints quickly.


## Key Dependencies (from `*.csproj`) 📦
- `BCrypt.Net-Next` — password hashing
- `DotNetEnv` — loads `.env` files into environment variables
- `Microsoft.AspNetCore.Authentication.JwtBearer` — JWT authentication
- `Microsoft.AspNetCore.OpenApi` — OpenAPI integration for development
- `MongoDB.Driver` — MongoDB client
- `System.IdentityModel.Tokens.Jwt` — JWT token handling

These are added in `Flowboard-Project-Management-System-Backend.csproj`.

---

## OpenAPI / Development UI
OpenAPI is enabled in development (`Program.cs` uses `AddOpenApi()` and `MapOpenApi()`). You can view the generated OpenAPI docs during development.

---

## Contributing
Feel free to open issues or create PRs to improve the API, add tests, or harden security (rate-limiting, stronger validation, roles, etc.).

---

Made for Flowboard — lightweight project management API. 🚀

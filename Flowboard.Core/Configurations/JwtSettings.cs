using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Flowboard_Project_Management_System_Backend.Configurations
{
    /// <summary>
    /// Reusable JWT validation-parameter construction, shared between hosting models
    /// (ASP.NET Core middleware, Azure Functions isolated-worker middleware).
    /// Reads the same environment variables the app has always used:
    /// JWT_KEY, JWT_ISSUER, JWT_AUDIENCE.
    /// </summary>
    public static class JwtSettings
    {
        public static TokenValidationParameters BuildValidationParameters()
        {
            var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? string.Empty;
            var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? string.Empty;
            var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? string.Empty;

            var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
            };
        }
    }
}

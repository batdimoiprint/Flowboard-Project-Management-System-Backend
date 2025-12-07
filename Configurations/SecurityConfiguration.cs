using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Flowboard_Project_Management_System_Backend.Configurations
{
    public static class SecurityConfiguration
    {
        // Adds a named CORS policy "AllowFrontend" and uses the environment to decide whether
        // to allow any origin (development) or a production origin.
        public static IServiceCollection AddFrontendCors(this IServiceCollection services, IHostEnvironment environment)
        {
            var productionFrontendOrigin = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? string.Empty;

            services.AddCors(options =>
            {
                options.AddPolicy("AllowFrontend", policy =>
                {
                    if (environment.IsDevelopment())
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyHeader()
                              .AllowAnyMethod();
                    }
                    else
                    {
                        policy.WithOrigins(productionFrontendOrigin)
                              .AllowAnyHeader()
                              .AllowAnyMethod();
                    }
                });
            });

            return services;
        }

        // Adds JWT Authentication services using environment variables for keys/issuer/audience.
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
        {
            var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? string.Empty;
            var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? string.Empty;
            var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? string.Empty;

            var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
                };
            });

            // Register Authorization as well, so callers don't have to explicitly add it
            services.AddAuthorization(options =>
            {
                // DetailedTask authorization policies
                options.AddPolicy("DetailedTaskCreate", policy =>
                {
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy("DetailedTaskEdit", policy =>
                {
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy("DetailedTaskUpdate", policy =>
                {
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy("DetailedTaskDelete", policy =>
                {
                    policy.RequireAuthenticatedUser();
                });

                // Client role - read-only access to assigned projects and tasks
                options.AddPolicy("ClientReadOnly", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole("Client");
                });

                // Project read access for clients
                options.AddPolicy("ProjectRead", policy =>
                {
                    policy.RequireAuthenticatedUser();
                });
            });

            return services;
        }
    }
}

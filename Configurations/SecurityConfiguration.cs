using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
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
                        // In development allow all origins but allow credentials, so we reflect the origin
                        // with SetIsOriginAllowed. Avoid AllowAnyOrigin together with AllowCredentials.
                        policy.SetIsOriginAllowed(_ => true)
                              .AllowAnyHeader()
                              .AllowAnyMethod()
                              .AllowCredentials();
                    }
                    else
                    {
                        // In production only allow configured frontend origin and credentials
                        if (!string.IsNullOrWhiteSpace(productionFrontendOrigin))
                        {
                            policy.WithOrigins(productionFrontendOrigin)
                                  .AllowAnyHeader()
                                  .AllowAnyMethod()
                                  .AllowCredentials();
                        }
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

                // Allow reading token from an HttpOnly cookie named "jwt" if present
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token))
                        {
                            // Try reading from the cookie
                            if (context.Request.Cookies.TryGetValue("jwt", out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
                            {
                                context.Token = cookieToken;
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            // Register Authorization as well, so callers don't have to explicitly add it
            services.AddAuthorization();

            return services;
        }
    }
}

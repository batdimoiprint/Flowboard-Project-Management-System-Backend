using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Flowboard_Project_Management_System_Backend.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Flowboard.Functions.Middleware
{
    /// <summary>
    /// DEFAULT-DENY JWT authentication for the isolated worker.
    /// Azure Functions runs none of the ASP.NET Core pipeline, so [Authorize] does nothing here.
    /// Every HTTP-triggered function is treated as protected unless its method is decorated
    /// with [Flowboard.Functions.Middleware.AllowAnonymous]. A missing/forgotten attribute
    /// therefore over-protects (401) rather than silently exposing an endpoint.
    ///
    /// The app's real authorization surface is a single rule: "valid token or 401". The five
    /// ASP.NET Core policies in the legacy app (DetailedTaskCreate/Edit/Update/Delete, ProjectRead)
    /// are all RequireAuthenticatedUser() with no additional logic, and ClientReadOnly (the only
    /// policy with a role check) is never applied anywhere in the controllers. So there is
    /// intentionally no policy engine here - just authenticate-or-reject.
    /// </summary>
    public sealed class JwtAuthMiddleware : IFunctionsWorkerMiddleware
    {
        private static readonly TokenValidationParameters ValidationParameters =
            JwtSettings.BuildValidationParameters();

        private static readonly JwtSecurityTokenHandler TokenHandler = new();

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var logger = context.GetLogger<JwtAuthMiddleware>();
            var httpContext = context.GetHttpContext();

            // Non-HTTP triggers (e.g. timer functions) pass straight through.
            if (httpContext is null)
            {
                await next(context);
                return;
            }

            // CORS preflight requests carry no Authorization header; CorsMiddleware
            // (registered before this one) already short-circuits OPTIONS, but guard
            // here too in case ordering ever changes.
            if (HttpMethods.IsOptions(httpContext.Request.Method))
            {
                await next(context);
                return;
            }

            if (IsAnonymousAllowed(context, logger))
            {
                await next(context);
                return;
            }

            var token = ExtractBearerToken(httpContext.Request);
            if (string.IsNullOrWhiteSpace(token))
            {
                await Deny(httpContext, "Missing bearer token.");
                return;
            }

            try
            {
                var principal = TokenHandler.ValidateToken(token, ValidationParameters, out _);

                // Preserve the role claim explicitly so downstream code that checks
                // ClaimTypes.Role behaves the same as it did under ASP.NET Core auth.
                httpContext.User = principal;
                context.Items["User"] = principal;

                await next(context);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "JWT validation failed.");
                await Deny(httpContext, "Invalid or expired token.");
            }
        }

        private static bool IsAnonymousAllowed(FunctionContext context, ILogger logger)
        {
            // Fail closed: if we can't determine the target method for any reason,
            // treat the endpoint as protected rather than accidentally exposing it.
            //
            // There is no FunctionContext.GetTargetFunctionMethod() helper in the worker
            // package versions this project pins (Worker 2.52.0 / Worker.Core 2.52.0), so the
            // target method is resolved from FunctionDefinition.EntryPoint directly - a string
            // of the form "Namespace.Type.MethodName" - by reflecting over the assemblies
            // already loaded into this process (the function app's own entry assembly, which
            // hosts this middleware, is always among them).
            try
            {
                var method = ResolveTargetMethod(context.FunctionDefinition.EntryPoint);
                return method?.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve target function method for [AllowAnonymous] check; denying by default.");
                return false;
            }
        }

        private static MethodInfo? ResolveTargetMethod(string entryPoint)
        {
            var lastDot = entryPoint.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == entryPoint.Length - 1)
            {
                return null;
            }

            var typeName = entryPoint[..lastDot];
            var methodName = entryPoint[(lastDot + 1)..];

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type;
                try
                {
                    type = assembly.GetType(typeName, throwOnError: false);
                }
                catch
                {
                    continue;
                }

                if (type is null)
                {
                    continue;
                }

                var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == methodName);

                if (method is not null)
                {
                    return method;
                }
            }

            return null;
        }

        private static string? ExtractBearerToken(HttpRequest request)
        {
            var header = request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return header[prefix.Length..].Trim();
        }

        private static async Task Deny(HttpContext httpContext, string message)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync($"{{\"error\":\"{message}\"}}");
        }
    }
}

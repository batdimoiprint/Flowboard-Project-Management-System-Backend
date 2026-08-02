using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Flowboard.Functions.Middleware
{
    /// <summary>
    /// Fixed-window rate limiter, keyed by client IP, entirely in-memory.
    /// This is per-instance only: on a scaled-out Function App each instance keeps its own
    /// counters, so the effective global limit is (limit x instance count). That is an
    /// accepted, intentional trade-off for this stage of the migration - not a bug.
    ///
    /// Configurable via:
    ///   RATE_LIMIT_REQUESTS         - max requests per window (default 100)
    ///   RATE_LIMIT_WINDOW_SECONDS   - window length in seconds (default 60)
    /// </summary>
    public sealed class RateLimitMiddleware : IFunctionsWorkerMiddleware
    {
        private static readonly int MaxRequests = ParseIntEnv("RATE_LIMIT_REQUESTS", 100);
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(ParseIntEnv("RATE_LIMIT_WINDOW_SECONDS", 60));

        private static readonly ConcurrentDictionary<string, WindowCounter> Counters = new();

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var httpContext = context.GetHttpContext();
            if (httpContext is null)
            {
                await next(context);
                return;
            }

            var clientId = GetClientIp(httpContext);
            var counter = Counters.GetOrAdd(clientId, _ => new WindowCounter());

            if (counter.TryIncrement(Window, MaxRequests, out var retryAfterSeconds))
            {
                await next(context);
                return;
            }

            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsync("{\"error\":\"Rate limit exceeded. Try again later.\"}");
        }

        private static string GetClientIp(HttpContext httpContext)
        {
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                // X-Forwarded-For can be a comma-separated list; the first entry is the client.
                return StripPort(forwardedFor.Split(',')[0].Trim());
            }

            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        /// <summary>
        /// Azure App Service / Functions writes X-Forwarded-For as "ip:port", and the source port
        /// changes on every connection. Keying the limiter on the raw value therefore produced a
        /// brand-new counter per request and the limit never tripped - verified live: 110 rapid
        /// requests against a 100/60s limit returned 110x 200 and zero 429s.
        /// </summary>
        private static string StripPort(string address)
        {
            if (string.IsNullOrEmpty(address)) return "unknown";

            // IPv6 arrives bracketed as "[::1]:port"; bare "::1" has no port to strip.
            if (address.StartsWith('['))
            {
                var close = address.IndexOf(']');
                return close > 0 ? address[1..close] : address;
            }

            // Only strip a trailing :port for IPv4 - a lone ':' in an unbracketed address means IPv6.
            var lastColon = address.LastIndexOf(':');
            if (lastColon > 0 && address.IndexOf(':') == lastColon)
            {
                return address[..lastColon];
            }

            return address;
        }

        private static int ParseIntEnv(string name, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
        }

        private sealed class WindowCounter
        {
            private int _count;
            private DateTime _windowStart = DateTime.UtcNow;
            private readonly object _lock = new();

            public bool TryIncrement(TimeSpan window, int max, out int retryAfterSeconds)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    if (now - _windowStart >= window)
                    {
                        _windowStart = now;
                        _count = 0;
                    }

                    if (_count >= max)
                    {
                        var elapsed = now - _windowStart;
                        var remaining = window - elapsed;
                        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
                        return false;
                    }

                    _count++;
                    retryAfterSeconds = 0;
                    return true;
                }
            }
        }
    }
}

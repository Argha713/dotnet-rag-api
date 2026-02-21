using System.Diagnostics;
using Serilog.Context;

namespace RagApi.Api.Middleware;

// Argha - 2026-02-15 - Logs method, path, status code, and elapsed time for every request
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Argha - 2026-02-21 - Read or generate correlation ID; propagate on response header and push to Serilog LogContext (Phase 6.1)
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            var stopwatch = Stopwatch.StartNew();

            await _next(context);

            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var method = context.Request.Method;
            var path = context.Request.Path;

            if (statusCode >= 500)
            {
                _logger.LogError("{Method} {Path} responded {StatusCode} in {Elapsed}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else if (statusCode >= 400)
            {
                _logger.LogWarning("{Method} {Path} responded {StatusCode} in {Elapsed}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("{Method} {Path} responded {StatusCode} in {Elapsed}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}

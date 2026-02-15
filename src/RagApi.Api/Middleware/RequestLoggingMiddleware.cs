using System.Diagnostics;

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

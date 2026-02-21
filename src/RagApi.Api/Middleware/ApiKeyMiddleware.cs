using System.Text.Json;

namespace RagApi.Api.Middleware;

// Argha - 2026-02-20 - API key authentication middleware 
// Reads X-Api-Key header and returns 401 if the key is missing or incorrect.
// Bypassed entirely when ApiAuth:ApiKey is empty (development mode).
// The health check path is always public regardless of key configuration.
public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string HealthPath = "/api/system/health";

    private readonly RequestDelegate _next;
    private readonly string? _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuredKey = configuration["ApiAuth:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Argha - 2026-02-20 - Bypass when no key is configured 
        if (string.IsNullOrEmpty(_configuredKey))
        {
            await _next(context);
            return;
        }

        // Argha - 2026-02-20 - Health check is always public 
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || string.IsNullOrEmpty(providedKey)
            || providedKey != _configuredKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = "Unauthorized",
                message = "Missing or invalid API key. Provide a valid key in the X-Api-Key header."
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

            return;
        }

        await _next(context);
    }
}

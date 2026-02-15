using System.Net;
using System.Text.Json;

namespace RagApi.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            NotSupportedException ex => (HttpStatusCode.UnsupportedMediaType, ex.Message),
            InvalidOperationException ex => (HttpStatusCode.BadRequest, ex.Message),
            ArgumentNullException ex => (HttpStatusCode.BadRequest, ex.Message),
            ArgumentException ex => (HttpStatusCode.BadRequest, ex.Message),
            KeyNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            OperationCanceledException => (HttpStatusCode.BadRequest, "The request was cancelled."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception ({StatusCode}) processing {Method} {Path}",
                (int)statusCode, context.Request.Method, context.Request.Path);
        }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = message,
            statusCode = (int)statusCode
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

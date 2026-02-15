using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Middleware;

namespace RagApi.Tests.Unit.Middleware;

// Argha - 2026-02-15 - Unit tests for GlobalExceptionMiddleware exception-to-HTTP mapping (Phase 1.5)
public class GlobalExceptionMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionMiddleware>> _loggerMock;

    public GlobalExceptionMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<GlobalExceptionMiddleware>>();
    }

    [Fact]
    public async Task NotSupportedException_Returns415()
    {
        var (statusCode, _) = await InvokeWithException(new NotSupportedException("Unsupported file type"));
        statusCode.Should().Be(415);
    }

    [Fact]
    public async Task InvalidOperationException_Returns400()
    {
        var (statusCode, _) = await InvokeWithException(new InvalidOperationException("Bad operation"));
        statusCode.Should().Be(400);
    }

    [Fact]
    public async Task ArgumentException_Returns400()
    {
        var (statusCode, _) = await InvokeWithException(new ArgumentException("Bad argument"));
        statusCode.Should().Be(400);
    }

    [Fact]
    public async Task KeyNotFoundException_Returns404()
    {
        var (statusCode, _) = await InvokeWithException(new KeyNotFoundException("Not found"));
        statusCode.Should().Be(404);
    }

    [Fact]
    public async Task UnhandledException_Returns500()
    {
        var (statusCode, body) = await InvokeWithException(new Exception("Something broke"));
        statusCode.Should().Be(500);
        body.Should().Contain("unexpected error");
    }

    [Fact]
    public async Task ReturnsJsonErrorBody()
    {
        var (_, body) = await InvokeWithException(new InvalidOperationException("Test error"));

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.GetProperty("error").GetString().Should().Be("Test error");
        json.GetProperty("statusCode").GetInt32().Should().Be(400);
    }

    private async Task<(int StatusCode, string Body)> InvokeWithException(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        RequestDelegate next = _ => throw exception;
        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }
}

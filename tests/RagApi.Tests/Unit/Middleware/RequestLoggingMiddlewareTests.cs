using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Middleware;

namespace RagApi.Tests.Unit.Middleware;

// Argha - 2026-02-15 - Unit tests for RequestLoggingMiddleware log level selection (Phase 1.5)
public class RequestLoggingMiddlewareTests
{
    private readonly Mock<ILogger<RequestLoggingMiddleware>> _loggerMock;

    public RequestLoggingMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<RequestLoggingMiddleware>>();
    }

    [Fact]
    public async Task Success_LogsInformation()
    {
        // Arrange
        await InvokeWithStatusCode(200);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ClientError_LogsWarning()
    {
        // Arrange
        await InvokeWithStatusCode(404);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ServerError_LogsError()
    {
        // Arrange
        await InvokeWithStatusCode(500);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // Argha - 2026-02-21 - Correlation ID tests for Phase 6.1

    [Fact]
    public async Task Invoke_NoCorrelationIdHeader_SetsNewCorrelationIdOnResponse()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        // No X-Correlation-ID on request

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var middleware = new RequestLoggingMiddleware(next, _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var responseHeader = context.Response.Headers["X-Correlation-ID"].ToString();
        responseHeader.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Invoke_WithExistingCorrelationIdHeader_EchoesIdOnResponse()
    {
        // Arrange
        const string existingId = "my-test-correlation-id";
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/test";
        context.Request.Headers["X-Correlation-ID"] = existingId;

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var middleware = new RequestLoggingMiddleware(next, _loggerMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be(existingId);
    }

    private async Task InvokeWithStatusCode(int statusCode)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/test";

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };

        var middleware = new RequestLoggingMiddleware(next, _loggerMock.Object);
        await middleware.InvokeAsync(context);
    }
}

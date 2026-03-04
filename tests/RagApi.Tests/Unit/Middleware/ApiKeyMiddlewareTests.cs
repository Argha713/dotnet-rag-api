using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RagApi.Api.Middleware;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Middleware;

// Argha - 2026-02-20 - Unit tests for ApiKeyMiddleware
public class ApiKeyMiddlewareTests
{
    private const string ValidKey = "test-api-key-123";

    // Argha - 2026-02-20 - Helper: build IConfiguration with a given ApiKey value
    private static IConfiguration BuildConfig(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ApiAuth:ApiKey", apiKey }
            })
            .Build();

    private static async Task<(int StatusCode, string Body)> InvokeAsync(
        IConfiguration config,
        string? providedKey,
        string path = "/api/chat")
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;

        if (providedKey is not null)
            context.Request.Headers["X-Api-Key"] = providedKey;

        // Argha - 2026-03-04 - #17 - Middleware now resolves IWorkspaceContext and IWorkspaceRepository
        // from RequestServices; supply mocked services so tests remain self-contained
        var workspaceContextMock = new Mock<IWorkspaceContext>();
        var workspaceRepoMock = new Mock<IWorkspaceRepository>();
        workspaceRepoMock
            .Setup(r => r.GetByApiKeyHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);
        workspaceRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null); // triggers CreateFallbackWorkspace in middleware

        var services = new ServiceCollection();
        services.AddSingleton(workspaceContextMock.Object);
        services.AddSingleton(workspaceRepoMock.Object);
        context.RequestServices = services.BuildServiceProvider();

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        var middleware = new ApiKeyMiddleware(next, config);
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task NoKeyConfigured_AllowsRequest()
    {
        var config = BuildConfig(string.Empty);
        var (status, _) = await InvokeAsync(config, providedKey: null);
        status.Should().Be(200);
    }

    [Fact]
    public async Task ValidKey_AllowsRequest()
    {
        var config = BuildConfig(ValidKey);
        var (status, _) = await InvokeAsync(config, providedKey: ValidKey);
        status.Should().Be(200);
    }

    [Fact]
    public async Task MissingHeader_Returns401()
    {
        var config = BuildConfig(ValidKey);
        var (status, _) = await InvokeAsync(config, providedKey: null);
        status.Should().Be(401);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var config = BuildConfig(ValidKey);
        var (status, _) = await InvokeAsync(config, providedKey: "wrong-key");
        status.Should().Be(401);
    }

    [Fact]
    public async Task EmptyHeaderValue_Returns401()
    {
        var config = BuildConfig(ValidKey);
        var (status, _) = await InvokeAsync(config, providedKey: string.Empty);
        status.Should().Be(401);
    }

    [Fact]
    public async Task HealthEndpoint_SkipsAuth()
    {
        var config = BuildConfig(ValidKey);
        var (status, _) = await InvokeAsync(config, providedKey: null, path: "/api/system/health");
        status.Should().Be(200);
    }

    [Fact]
    public async Task Returns401_WithJsonBody()
    {
        var config = BuildConfig(ValidKey);
        var (status, body) = await InvokeAsync(config, providedKey: "wrong");

        status.Should().Be(401);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.GetProperty("error").GetString().Should().Be("Unauthorized");
        json.GetProperty("message").GetString().Should().Contain("X-Api-Key");
    }
}

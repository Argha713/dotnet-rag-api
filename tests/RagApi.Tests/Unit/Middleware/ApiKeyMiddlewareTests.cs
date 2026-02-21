using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RagApi.Api.Middleware;

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

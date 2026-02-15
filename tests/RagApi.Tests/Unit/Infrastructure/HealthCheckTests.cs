using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RagApi.Infrastructure;
using RagApi.Infrastructure.Data;
using RagApi.Infrastructure.HealthChecks;
using System.Net;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-02-15 - Unit tests for Ollama and SQLite health checks (Phase 1.5)
public class HealthCheckTests
{
    [Fact]
    public async Task OllamaHealthCheck_Success_ReturnsHealthy()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var config = Options.Create(new AiConfiguration
        {
            Ollama = new OllamaSettings { BaseUrl = "http://localhost:11434" }
        });

        var healthCheck = new OllamaHealthCheck(
            factoryMock.Object, config, Mock.Of<ILogger<OllamaHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Ollama reachable");
    }

    [Fact]
    public async Task OllamaHealthCheck_Failure_ReturnsUnhealthy()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var config = Options.Create(new AiConfiguration
        {
            Ollama = new OllamaSettings { BaseUrl = "http://localhost:11434" }
        });

        var healthCheck = new OllamaHealthCheck(
            factoryMock.Object, config, Mock.Of<ILogger<OllamaHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Ollama unreachable");
    }

    [Fact]
    public async Task SqliteHealthCheck_CanConnect_ReturnsHealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new RagApiDbContext(options);
        var healthCheck = new SqliteHealthCheck(dbContext, Mock.Of<ILogger<SqliteHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("SQLite database reachable");
    }

    [Fact]
    public async Task SqliteHealthCheck_CannotConnect_ReturnsUnhealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new RagApiDbContext(options);
        dbContext.Dispose(); // Argha - 2026-02-15 - Force disposal so CanConnectAsync fails

        var healthCheck = new SqliteHealthCheck(dbContext, Mock.Of<ILogger<SqliteHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}

// Argha - 2026-03-16 - #32 - Unit tests for OpenAIVisionService
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RagApi.Infrastructure;
using RagApi.Infrastructure.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RagApi.Tests.Unit.Infrastructure;

public class OpenAIVisionServiceTests
{
    private static OpenAIVisionService CreateService(
        HttpMessageHandler handler,
        string apiKey = "test-key",
        string visionModel = "gpt-4o-mini")
    {
        var httpClient = new HttpClient(handler);
        var aiOptions = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings
            {
                ApiKey = apiKey,
                BaseUrl = "https://api.openai.com/v1",
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-small",
                EmbeddingDimension = 1536
            }
        });
        var visionOptions = Options.Create(new VisionConfiguration
        {
            Enabled = true,
            Model = visionModel
        });
        return new OpenAIVisionService(httpClient, aiOptions, visionOptions, Mock.Of<ILogger<OpenAIVisionService>>());
    }

    private static HttpResponseMessage OkVisionResponse(
        string description = "A test image.",
        int promptTokens = 100,
        int completionTokens = 50)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = description } } },
                    usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens }
                }),
                Encoding.UTF8, "application/json")
        };

    // Argha - 2026-03-16 - #32 - IsEnabled is always true when OpenAIVisionService is registered
    [Fact]
    public void IsEnabled_ReturnsTrue()
    {
        var sut = CreateService(new Mock<HttpMessageHandler>().Object);

        sut.IsEnabled.Should().BeTrue();
    }

    // Argha - 2026-03-16 - #32 - Image bytes must arrive at OpenAI as a base64 data URI
    [Fact]
    public async Task DescribeImageAsync_SendsBase64DataUri()
    {
        // Arrange
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkVisionResponse());

        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF };
        var expectedBase64 = Convert.ToBase64String(imageBytes);
        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.DescribeImageAsync(imageBytes, "image/jpeg");

        // Assert
        capturedBody.Should().Contain($"data:image/jpeg;base64,{expectedBase64}");
    }

    // Argha - 2026-03-16 - #32 - System prompt must contain key instruction phrase
    [Fact]
    public async Task DescribeImageAsync_SendsCorrectSystemPrompt()
    {
        // Arrange
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkVisionResponse());

        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.DescribeImageAsync(new byte[] { 1 }, "image/png");

        // Assert
        capturedBody.Should().Contain("technical document for a search index");
    }

    // Argha - 2026-03-16 - #32 - Description from OpenAI choices[0].message.content is returned verbatim
    [Fact]
    public async Task DescribeImageAsync_ReturnsDescriptionFromResponse()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkVisionResponse("A diagram showing step 3 of installation."));

        var sut = CreateService(handlerMock.Object);

        // Act
        var result = await sut.DescribeImageAsync(new byte[] { 1, 2 }, "image/png");

        // Assert
        result.Should().Be("A diagram showing step 3 of installation.");
    }

    // Argha - 2026-03-16 - #32 - Non-null context is included as a text content part in the user message
    [Fact]
    public async Task DescribeImageAsync_WithContext_IncludesTextPart()
    {
        // Arrange
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkVisionResponse());

        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.DescribeImageAsync(new byte[] { 1 }, "image/png", context: "Figure 2: Network topology");

        // Assert
        capturedBody.Should().Contain("Figure 2: Network topology");
    }

    // Argha - 2026-03-16 - #32 - Token counts logged at Information for Phase 13 cost tracking
    [Fact]
    public async Task DescribeImageAsync_LogsTokenUsage()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<OpenAIVisionService>>();
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkVisionResponse(promptTokens: 200, completionTokens: 75));

        var httpClient = new HttpClient(handlerMock.Object);
        var aiOptions = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings { ApiKey = "test-key", BaseUrl = "https://api.openai.com/v1" }
        });
        var visionOptions = Options.Create(new VisionConfiguration { Enabled = true, Model = "gpt-4o-mini" });
        var sut = new OpenAIVisionService(httpClient, aiOptions, visionOptions, loggerMock.Object);

        // Act
        await sut.DescribeImageAsync(new byte[] { 1 }, "image/png");

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("200") && v.ToString()!.Contains("75")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

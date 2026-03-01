// Argha - 2026-03-01 - Unit tests for OpenAiChatService (Phase 7)
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RagApi.Domain.Entities;
using RagApi.Infrastructure;
using RagApi.Infrastructure.AI;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RagApi.Tests.Unit.Infrastructure;

public class OpenAiChatServiceTests
{
    private static OpenAiChatService CreateService(HttpMessageHandler handler, string apiKey = "test-key", string chatModel = "gpt-4o-mini")
    {
        var httpClient = new HttpClient(handler);
        var config = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings
            {
                ApiKey = apiKey,
                BaseUrl = "https://api.openai.com/v1",
                ChatModel = chatModel,
                EmbeddingModel = "text-embedding-3-small",
                EmbeddingDimension = 1536
            }
        });
        return new OpenAiChatService(httpClient, config, Mock.Of<ILogger<OpenAiChatService>>());
    }

    [Fact]
    public void ModelName_ReturnsConfiguredChatModel()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var sut = CreateService(handler.Object, chatModel: "gpt-4o-mini");

        // Act & Assert
        sut.ModelName.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task GenerateResponseAsync_SendsBearerAuthHeader()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = "Hello!" } } }
                }), Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.GenerateResponseAsync("sys", new List<ChatMessage>());

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("test-key");
    }

    [Fact]
    public async Task GenerateResponseAsync_PostsToChatCompletionsEndpoint()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = "ok" } } }
                }), Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.GenerateResponseAsync("sys", new List<ChatMessage>());

        // Assert
        capturedRequest!.RequestUri!.ToString().Should().Contain("chat/completions");
        capturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GenerateResponseAsync_IncludesModelInRequestBody()
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
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = "ok" } } }
                }), Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handlerMock.Object, chatModel: "gpt-4o-mini");

        // Act
        await sut.GenerateResponseAsync("sys", new List<ChatMessage>());

        // Assert
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("gpt-4o-mini");
    }

    [Fact]
    public async Task GenerateResponseAsync_ReturnsAssistantContent()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = "The answer is 42." } } }
                }), Encoding.UTF8, "application/json")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        var result = await sut.GenerateResponseAsync("sys", new List<ChatMessage>());

        // Assert
        result.Should().Be("The answer is 42.");
    }

    [Fact]
    public async Task GenerateResponseAsync_HttpError_ThrowsException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"invalid api key\"}")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        var act = async () => await sut.GenerateResponseAsync("sys", new List<ChatMessage>());

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GenerateResponseStreamAsync_YieldsTokensFromSseChunks()
    {
        // Arrange
        var sseBody =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        var tokens = new List<string>();
        await foreach (var token in sut.GenerateResponseStreamAsync("sys", new List<ChatMessage>()))
            tokens.Add(token);

        // Assert
        tokens.Should().Equal("Hello", " world");
    }
}

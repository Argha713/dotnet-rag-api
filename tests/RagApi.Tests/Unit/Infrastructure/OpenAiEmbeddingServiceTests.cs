// Argha - 2026-03-01 - Unit tests for OpenAiEmbeddingService (Phase 7)
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

public class OpenAiEmbeddingServiceTests
{
    private static OpenAiEmbeddingService CreateService(HttpMessageHandler handler, string apiKey = "test-key", string embeddingModel = "text-embedding-3-small")
    {
        var httpClient = new HttpClient(handler);
        var config = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings
            {
                ApiKey = apiKey,
                BaseUrl = "https://api.openai.com/v1",
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = embeddingModel,
                EmbeddingDimension = 1536
            }
        });
        return new OpenAiEmbeddingService(httpClient, config, Mock.Of<ILogger<OpenAiEmbeddingService>>());
    }

    private static HttpResponseMessage MakeEmbeddingResponse(params float[][] embeddings)
    {
        var data = embeddings.Select((e, i) => new { index = i, embedding = e, @object = "embedding" }).ToList();
        var body = JsonSerializer.Serialize(new { @object = "list", data });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public void EmbeddingDimension_ReturnsConfiguredValue()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var sut = CreateService(handler.Object);

        // Act & Assert
        sut.EmbeddingDimension.Should().Be(1536);
    }

    [Fact]
    public void ModelName_ReturnsConfiguredEmbeddingModel()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var sut = CreateService(handler.Object, embeddingModel: "text-embedding-3-small");

        // Act & Assert
        sut.ModelName.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SendsBearerAuthHeader()
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
            .ReturnsAsync(MakeEmbeddingResponse(new float[] { 0.1f, 0.2f }));

        var sut = CreateService(handlerMock.Object, apiKey: "sk-test");

        // Act
        await sut.GenerateEmbeddingsAsync(new List<string> { "hello" });

        // Assert
        capturedRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_PostsToEmbeddingsEndpoint()
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
            .ReturnsAsync(MakeEmbeddingResponse(new float[] { 0.1f }));

        var sut = CreateService(handlerMock.Object);

        // Act
        await sut.GenerateEmbeddingsAsync(new List<string> { "test" });

        // Assert
        capturedRequest!.RequestUri!.ToString().Should().Contain("embeddings");
        capturedRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_IncludesModelInRequestBody()
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
            .ReturnsAsync(MakeEmbeddingResponse(new float[] { 0.1f }));

        var sut = CreateService(handlerMock.Object, embeddingModel: "text-embedding-3-small");

        // Act
        await sut.GenerateEmbeddingsAsync(new List<string> { "text" });

        // Assert
        capturedBody.Should().Contain("text-embedding-3-small");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_ReturnsCorrectEmbeddingVectors()
    {
        // Arrange
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(MakeEmbeddingResponse(expectedEmbedding));

        var sut = CreateService(handlerMock.Object);

        // Act
        var result = await sut.GenerateEmbeddingsAsync(new List<string> { "hello" });

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().BeEquivalentTo(expectedEmbedding);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_SingleText_ReturnsSingleVector()
    {
        // Arrange
        var expectedEmbedding = new float[] { 0.5f, 0.6f };
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(MakeEmbeddingResponse(expectedEmbedding));

        var sut = CreateService(handlerMock.Object);

        // Act
        var result = await sut.GenerateEmbeddingAsync("single text");

        // Assert
        result.Should().BeEquivalentTo(expectedEmbedding);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_HttpError_ThrowsException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":\"rate limit exceeded\"}")
            });

        var sut = CreateService(handlerMock.Object);

        // Act
        var act = async () => await sut.GenerateEmbeddingsAsync(new List<string> { "text" });

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

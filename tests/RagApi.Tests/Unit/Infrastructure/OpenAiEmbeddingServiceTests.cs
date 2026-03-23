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
    private static OpenAiEmbeddingService CreateService(HttpMessageHandler handler, string apiKey = "test-key", string embeddingModel = "text-embedding-3-small", int batchSize = 100)
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
                EmbeddingDimension = 1536,
                EmbeddingBatchSize = batchSize
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

    // Argha - 2026-03-23 - #96 - Batching tests: verify large inputs are split into multiple API calls

    [Fact]
    public async Task GenerateEmbeddingsAsync_InputExceedsBatchSize_MakesMultipleHttpCalls()
    {
        // Arrange — batch size 2, 5 inputs → 3 HTTP calls (2+2+1)
        var callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return MakeEmbeddingResponse(new float[] { (float)callCount });
            });

        var sut = CreateService(handlerMock.Object, batchSize: 2);
        var texts = Enumerable.Range(0, 5).Select(i => $"text{i}").ToList();

        // Act
        await sut.GenerateEmbeddingsAsync(texts);

        // Assert
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_InputExceedsBatchSize_ReturnsAllEmbeddingsInOrder()
    {
        // Arrange — batch size 2, 4 inputs → 2 calls, each returning 2 embeddings
        var batch1 = new[] { new float[] { 0.1f }, new float[] { 0.2f } };
        var batch2 = new[] { new float[] { 0.3f }, new float[] { 0.4f } };
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            MakeEmbeddingResponse(batch1[0], batch1[1]),
            MakeEmbeddingResponse(batch2[0], batch2[1])
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => responses.Dequeue());

        var sut = CreateService(handlerMock.Object, batchSize: 2);
        var texts = Enumerable.Range(0, 4).Select(i => $"t{i}").ToList();

        // Act
        var result = await sut.GenerateEmbeddingsAsync(texts);

        // Assert — all 4 embeddings present in original order
        result.Should().HaveCount(4);
        result[0].Should().BeEquivalentTo(batch1[0]);
        result[1].Should().BeEquivalentTo(batch1[1]);
        result[2].Should().BeEquivalentTo(batch2[0]);
        result[3].Should().BeEquivalentTo(batch2[1]);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_InputFitsExactlyOneBatch_MakesOneHttpCall()
    {
        // Arrange — batch size 3, exactly 3 inputs → 1 call
        var callCount = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return MakeEmbeddingResponse(new float[] { 1f }, new float[] { 2f }, new float[] { 3f });
            });

        var sut = CreateService(handlerMock.Object, batchSize: 3);
        var texts = new List<string> { "a", "b", "c" };

        // Act
        var result = await sut.GenerateEmbeddingsAsync(texts);

        // Assert
        callCount.Should().Be(1);
        result.Should().HaveCount(3);
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-15 - Unit tests for RagService RAG pipeline orchestration (Phase 1.5)
public class RagServiceTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;
    private readonly RagService _sut;

    private static readonly float[] TestEmbedding = new float[768];

    public RagServiceTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _chatServiceMock = new Mock<IChatService>();
        _loggerMock = new Mock<ILogger<RagService>>();

        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEmbedding);

        _chatServiceMock.Setup(c => c.ModelName).Returns("llama3.2");

        _sut = new RagService(
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _chatServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ChatAsync_ReturnsAnswerWithSources()
    {
        // Arrange
        var searchResults = CreateSearchResults(2);
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test answer");

        // Act
        var result = await _sut.ChatAsync("What is RAG?");

        // Assert
        result.Answer.Should().Be("Test answer");
        result.Sources.Should().HaveCount(2);
        result.Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task ChatAsync_NoSearchResults_ReturnsFallbackMessage()
    {
        // Arrange
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var result = await _sut.ChatAsync("Unknown topic");

        // Assert
        result.Answer.Should().Contain("couldn't find any relevant information");
        result.Sources.Should().BeEmpty();
        _chatServiceMock.Verify(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_FiltersByDocumentId()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var searchResults = CreateSearchResults(1);
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Filtered answer");

        // Act
        await _sut.ChatAsync("question", filterByDocumentId: docId);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_IncludesConversationHistory()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi there" }
        };
        var searchResults = CreateSearchResults(1);
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer with context");

        // Act
        await _sut.ChatAsync("follow-up question", conversationHistory: history);

        // Assert
        _chatServiceMock.Verify(c => c.GenerateResponseAsync(
            It.IsAny<string>(),
            It.Is<List<ChatMessage>>(msgs => msgs.Count == 3 && msgs[0].Role == "user" && msgs[0].Content == "Hello"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChatAsync_TruncatesSourceText()
    {
        // Arrange
        var longContent = new string('A', 300);
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "test.txt", Content = longContent, Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer");

        // Act
        var result = await _sut.ChatAsync("query");

        // Assert
        result.Sources[0].RelevantText.Should().HaveLength(200);
        result.Sources[0].RelevantText.Should().EndWith("...");
    }

    [Fact]
    public async Task SearchAsync_ReturnsResults()
    {
        // Arrange
        var searchResults = CreateSearchResults(3);
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await _sut.SearchAsync("test query");

        // Assert
        result.Should().HaveCount(3);
        _embeddingServiceMock.Verify(e => e.GenerateEmbeddingAsync("test query", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        // Arrange
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 10, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        await _sut.SearchAsync("query", topK: 10);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 10, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_FiltersByDocumentId()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        await _sut.SearchAsync("query", filterByDocumentId: docId);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static List<SearchResult> CreateSearchResults(int count)
    {
        return Enumerable.Range(0, count).Select(i => new SearchResult
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = $"doc{i}.txt",
            Content = $"Content of chunk {i}",
            Score = 0.9 - (i * 0.1),
            ChunkIndex = i
        }).ToList();
    }
}

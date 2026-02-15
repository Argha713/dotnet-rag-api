using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-02-15 - Unit tests for ChatController endpoints (Phase 1.5)
public class ChatControllerTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IChatService> _chatMock;
    private readonly ChatController _sut;

    private static readonly float[] TestEmbedding = new float[768];

    public ChatControllerTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _chatMock = new Mock<IChatService>();

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEmbedding);
        _chatMock.Setup(c => c.ModelName).Returns("llama3.2");

        // Argha - 2026-02-15 - Use real RagService with mocked dependencies since it's a concrete class
        var ragService = new RagService(
            _vectorStoreMock.Object,
            _embeddingMock.Object,
            _chatMock.Object,
            Mock.Of<ILogger<RagService>>());

        _sut = new ChatController(ragService);
    }

    [Fact]
    public async Task Chat_ValidRequest_Returns200WithResponse()
    {
        // Arrange
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "relevant content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test answer");

        var request = new ChatRequest { Query = "What is AI?" };

        // Act
        var result = await _sut.Chat(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<ChatResponseDto>().Subject;
        dto.Answer.Should().Be("Test answer");
        dto.Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task Chat_MapsSourcesCorrectly()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = docId, FileName = "source.pdf", Content = "text", Score = 0.85, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer");

        // Act
        var result = await _sut.Chat(new ChatRequest { Query = "test" }, CancellationToken.None);

        // Assert
        var dto = ((OkObjectResult)result).Value as ChatResponseDto;
        dto!.Sources.Should().HaveCount(1);
        dto.Sources[0].DocumentId.Should().Be(docId);
        dto.Sources[0].FileName.Should().Be("source.pdf");
        dto.Sources[0].RelevanceScore.Should().Be(0.85);
    }

    [Fact]
    public async Task Search_ValidRequest_Returns200WithResults()
    {
        // Arrange
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        var request = new SearchRequest { Query = "test query" };

        // Act
        var result = await _sut.Search(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<List<SearchResultDto>>().Subject;
        dtos.Should().HaveCount(1);
        dtos[0].Content.Should().Be("content");
    }

    [Fact]
    public async Task Search_FiltersByDocumentId()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        var request = new SearchRequest { Query = "query", DocumentId = docId };

        // Act
        await _sut.Search(request, CancellationToken.None);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 5, docId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

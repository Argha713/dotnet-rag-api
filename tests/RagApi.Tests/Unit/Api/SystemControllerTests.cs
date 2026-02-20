using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;
using RagApi.Infrastructure;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-02-15 - Unit tests for SystemController stats endpoint (Phase 1.5)
public class SystemControllerTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IChatService> _chatMock;
    private readonly Mock<IDocumentRepository> _repositoryMock;
    private readonly SystemController _sut;

    public SystemControllerTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _chatMock = new Mock<IChatService>();
        _repositoryMock = new Mock<IDocumentRepository>();

        _embeddingMock.Setup(e => e.ModelName).Returns("nomic-embed-text");
        _embeddingMock.Setup(e => e.EmbeddingDimension).Returns(768);
        _chatMock.Setup(c => c.ModelName).Returns("llama3.2");

        var aiConfig = Options.Create(new AiConfiguration { Provider = "Ollama" });

        // Argha - 2026-02-15 - Use real DocumentService with mocked dependencies since it's a concrete class
        // Argha - 2026-02-20 - Pass default DocumentProcessingOptions (Phase 3.3)
        var documentService = new DocumentService(
            Mock.Of<IDocumentProcessor>(),
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<DocumentService>>(),
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()));

        _sut = new SystemController(
            _vectorStoreMock.Object,
            _embeddingMock.Object,
            _chatMock.Object,
            documentService,
            aiConfig,
            Mock.Of<ILogger<SystemController>>());
    }

    [Fact]
    public async Task GetStats_Returns200WithStats()
    {
        // Arrange
        _vectorStoreMock.Setup(v => v.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VectorStoreStats { TotalVectors = 42, CollectionName = "documents" });
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Document> { new(), new(), new() });

        // Act
        var result = await _sut.GetStats(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var stats = okResult.Value.Should().BeOfType<SystemStatsDto>().Subject;
        stats.TotalDocuments.Should().Be(3);
        stats.TotalVectors.Should().Be(42);
        stats.AiProvider.Should().Be("Ollama");
        stats.EmbeddingModel.Should().Be("nomic-embed-text");
        stats.ChatModel.Should().Be("llama3.2");
        stats.EmbeddingDimension.Should().Be(768);
    }

    [Fact]
    public async Task GetStats_VectorStoreFailure_ReturnsPartialStats()
    {
        // Arrange
        _vectorStoreMock.Setup(v => v.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Qdrant unreachable"));
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Document>());

        // Act
        var result = await _sut.GetStats(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var stats = okResult.Value.Should().BeOfType<SystemStatsDto>().Subject;
        stats.TotalVectors.Should().Be(0);
        stats.AiProvider.Should().Be("Ollama");
    }

    [Fact]
    public async Task GetStats_IncludesProviderInfo()
    {
        // Arrange
        _vectorStoreMock.Setup(v => v.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VectorStoreStats());
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Document>());

        // Act
        var result = await _sut.GetStats(CancellationToken.None);

        // Assert
        var stats = ((OkObjectResult)result).Value as SystemStatsDto;
        stats!.ChatModel.Should().Be("llama3.2");
        stats.EmbeddingDimension.Should().Be(768);
    }
}

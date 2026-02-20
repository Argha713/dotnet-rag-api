using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-15 - Unit tests for DocumentService upload lifecycle and CRUD (Phase 1.5)
public class DocumentServiceTests
{
    private readonly Mock<IDocumentProcessor> _processorMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<ILogger<DocumentService>> _loggerMock;
    private readonly Mock<IDocumentRepository> _repositoryMock;
    private readonly DocumentService _sut;

    public DocumentServiceTests()
    {
        _processorMock = new Mock<IDocumentProcessor>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _loggerMock = new Mock<ILogger<DocumentService>>();
        _repositoryMock = new Mock<IDocumentRepository>();

        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain", "application/pdf" });

        // Argha - 2026-02-20 - Pass default DocumentProcessingOptions (Phase 3.3)
        _sut = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()));
    }

    [Fact]
    public async Task UploadAsync_UnsupportedType_ThrowsNotSupported()
    {
        // Arrange
        _processorMock.Setup(p => p.IsSupported("image/png")).Returns(false);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var act = () => _sut.UploadDocumentAsync(stream, "photo.png", "image/png");

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*image/png*");
    }

    [Fact]
    public async Task UploadAsync_CompletesSuccessfully_StatusCompleted()
    {
        // Arrange
        SetupSuccessfulUpload("Hello world");

        using var stream = new MemoryStream();

        // Act
        var result = await _sut.UploadDocumentAsync(stream, "test.txt", "text/plain");

        // Assert
        result.Status.Should().Be(DocumentStatus.Completed);
        result.FileName.Should().Be("test.txt");
    }

    [Fact]
    public async Task UploadAsync_SavesToRepository()
    {
        // Arrange
        SetupSuccessfulUpload("Hello world");
        using var stream = new MemoryStream();

        // Act
        await _sut.UploadDocumentAsync(stream, "test.txt", "text/plain");

        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.Is<Document>(d => d.FileName == "test.txt"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_ExtractsText_ChunksAndEmbeds()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "chunk 1", Metadata = new Dictionary<string, string>() },
            new() { Content = "chunk 2", Metadata = new Dictionary<string, string>() }
        };
        var embeddings = new List<float[]> { new float[768], new float[768] };

        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some text content");
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), "Some text content", It.IsAny<ChunkingOptions?>()))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);

        using var stream = new MemoryStream();

        // Act
        await _sut.UploadDocumentAsync(stream, "test.txt", "text/plain");

        // Assert
        _processorMock.Verify(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()), Times.Once);
        _processorMock.Verify(p => p.ChunkText(It.IsAny<Guid>(), "Some text content", It.IsAny<ChunkingOptions?>()), Times.Once);
        _embeddingMock.Verify(e => e.GenerateEmbeddingsAsync(It.Is<List<string>>(l => l.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.UpsertChunksAsync(chunks, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_EmptyText_ThrowsInvalidOperation()
    {
        // Arrange
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("   ");
        using var stream = new MemoryStream();

        // Act
        var act = () => _sut.UploadDocumentAsync(stream, "empty.txt", "text/plain");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No text content*");
    }

    [Fact]
    public async Task UploadAsync_Error_SetsStatusFailed()
    {
        // Arrange
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Parse error"));
        using var stream = new MemoryStream();

        // Act
        var act = () => _sut.UploadDocumentAsync(stream, "bad.txt", "text/plain");

        // Assert
        await act.Should().ThrowAsync<Exception>();
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<Document>(d => d.Status == DocumentStatus.Failed && d.ErrorMessage == "Parse error"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDocumentAsync_Found_ReturnsDocument()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "test.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        // Act
        var result = await _sut.GetDocumentAsync(docId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(docId);
    }

    [Fact]
    public async Task GetDocumentAsync_NotFound_ReturnsNull()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.GetDocumentAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllDocumentsAsync_ReturnsOrderedList()
    {
        // Arrange
        var docs = new List<Document>
        {
            new() { FileName = "b.txt", UploadedAt = DateTime.UtcNow },
            new() { FileName = "a.txt", UploadedAt = DateTime.UtcNow.AddMinutes(-5) }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(docs);

        // Act
        var result = await _sut.GetAllDocumentsAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteDocumentAsync_RemovesChunksAndDocument()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act
        await _sut.DeleteDocumentAsync(docId);

        // Assert
        _vectorStoreMock.Verify(v => v.DeleteDocumentChunksAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Argha - 2026-02-19 - Tag-related tests (Phase 2.3)

    [Fact]
    public async Task UploadAsync_WithTags_StoresTagsInDocument()
    {
        // Arrange
        SetupSuccessfulUpload("Hello world");
        var tags = new List<string> { "finance", "2024" };
        using var stream = new MemoryStream();

        // Act
        var result = await _sut.UploadDocumentAsync(stream, "tagged.txt", "text/plain", tags);

        // Assert
        result.TagsJson.Should().Contain("finance");
        result.TagsJson.Should().Contain("2024");
    }

    [Fact]
    public async Task UploadAsync_WithTags_PropagatesTagsToChunks()
    {
        // Arrange
        var tags = new List<string> { "tech" };
        var capturedChunks = new List<DocumentChunk>();
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "content", Metadata = new Dictionary<string, string>() }
        };
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("content");
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), "content", It.IsAny<ChunkingOptions?>()))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[768] });
        _vectorStoreMock.Setup(v => v.UpsertChunksAsync(It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<List<DocumentChunk>, CancellationToken>((c, _) => capturedChunks.AddRange(c))
            .Returns(Task.CompletedTask);
        using var stream = new MemoryStream();

        // Act
        await _sut.UploadDocumentAsync(stream, "tagged.txt", "text/plain", tags);

        // Assert
        capturedChunks.Should().HaveCount(1);
        capturedChunks[0].Tags.Should().ContainSingle("tech");
    }

    [Fact]
    public async Task GetAllDocumentsAsync_WithTag_ReturnsOnlyTaggedDocuments()
    {
        // Arrange
        var docs = new List<Document>
        {
            new() { FileName = "tagged.txt", TagsJson = "[\"finance\",\"2024\"]" },
            new() { FileName = "other.txt", TagsJson = "[]" },
            new() { FileName = "also-tagged.txt", TagsJson = "[\"finance\"]" }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(docs);

        // Act
        var result = await _sut.GetAllDocumentsAsync("finance");

        // Assert
        result.Should().HaveCount(2);
        result.All(d => d.TagsJson.Contains("finance")).Should().BeTrue();
    }

    private void SetupSuccessfulUpload(string extractedText)
    {
        var chunks = new List<DocumentChunk>
        {
            new() { Content = extractedText, Metadata = new Dictionary<string, string>() }
        };
        var embeddings = new List<float[]> { new float[768] };

        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedText);
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), extractedText, It.IsAny<ChunkingOptions?>()))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);
    }
}

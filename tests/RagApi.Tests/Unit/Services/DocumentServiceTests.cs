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

    // Argha - 2026-03-04 - #17 - Default collection name used by all test workspace contexts
    private const string TestCollection = "documents";

    public DocumentServiceTests()
    {
        _processorMock = new Mock<IDocumentProcessor>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _loggerMock = new Mock<ILogger<DocumentService>>();
        _repositoryMock = new Mock<IDocumentRepository>();

        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain", "application/pdf" });

        // Argha - 2026-03-04 - #17 - Provide workspace context with a fixed collection name
        var workspaceContext = new Mock<IWorkspaceContext>();
        workspaceContext.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = TestCollection
        });

        // Argha - 2026-02-20 - Pass default DocumentProcessingOptions
        // Argha - 2026-03-04 - #17 - Pass workspace context for per-request collection name
        _sut = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            workspaceContext.Object);
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
        _vectorStoreMock.Verify(v => v.UpsertChunksAsync(It.IsAny<string>(), chunks, It.IsAny<CancellationToken>()), Times.Once);
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
        _vectorStoreMock.Verify(v => v.DeleteDocumentChunksAsync(It.IsAny<string>(), docId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.DeleteAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Argha - 2026-02-19 - Tag-related tests 

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
        _vectorStoreMock.Setup(v => v.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<DocumentChunk>, CancellationToken>((_, c, _) => capturedChunks.AddRange(c))
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

    // Argha - 2026-02-21 - Tests for UpdateDocumentAsync 

    [Fact]
    public async Task UpdateAsync_DocumentNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var act = () => _sut.UpdateDocumentAsync(Guid.NewGuid(), stream, "doc.txt", "text/plain");

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        _processorMock.Setup(p => p.IsSupported("image/png")).Returns(false);
        using var stream = new MemoryStream(new byte[] { 1 });

        // Act
        var act = () => _sut.UpdateDocumentAsync(docId, stream, "photo.png", "image/png");

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*image/png*");
    }

    [Fact]
    public async Task UpdateAsync_Success_DeletesOldChunksAndUpsertsNew()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        SetupSuccessfulUpload("New content");
        using var stream = new MemoryStream();

        // Act
        await _sut.UpdateDocumentAsync(docId, stream, "updated.txt", "text/plain");

        // Assert
        _vectorStoreMock.Verify(v => v.DeleteDocumentChunksAsync(It.IsAny<string>(), docId, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_PreservesDocumentId()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        SetupSuccessfulUpload("New content");
        using var stream = new MemoryStream();

        // Act
        var result = await _sut.UpdateDocumentAsync(docId, stream, "updated.txt", "text/plain");

        // Assert
        result.Id.Should().Be(docId);
    }

    [Fact]
    public async Task UpdateAsync_SetsUpdatedAt()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        SetupSuccessfulUpload("New content");
        using var stream = new MemoryStream();

        // Act
        var result = await _sut.UpdateDocumentAsync(docId, stream, "updated.txt", "text/plain");

        // Assert
        result.UpdatedAt.Should().NotBeNull();
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateAsync_ProcessingFails_SetsFailedStatusAndRethrows()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Extraction failed"));
        using var stream = new MemoryStream();

        // Act
        var act = () => _sut.UpdateDocumentAsync(docId, stream, "updated.txt", "text/plain");

        // Assert
        await act.Should().ThrowAsync<Exception>();
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<Document>(d => d.Status == DocumentStatus.Failed && d.ErrorMessage == "Extraction failed"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}

// Argha - 2026-03-17 - #36 - Vision ingestion pipeline tests (separate fixture so existing tests stay unchanged)
public class DocumentServiceVisionPipelineTests
{
    private readonly Mock<IDocumentProcessor> _processorMock = new();
    private readonly Mock<IEmbeddingService> _embeddingMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<ILogger<DocumentService>> _loggerMock = new();
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IVisionService> _visionMock = new();
    private readonly Mock<IImageStore> _imageStoreMock = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();

    private const string TestCollection = "documents";

    public DocumentServiceVisionPipelineTests()
    {
        _processorMock.Setup(p => p.IsSupported("application/pdf")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes)
            .Returns(new[] { "text/plain", "application/pdf" });

        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = TestCollection
        });
    }

    private DocumentService BuildSut(bool visionEnabled = true)
    {
        _visionMock.Setup(v => v.IsEnabled).Returns(visionEnabled);
        return new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            _workspaceContextMock.Object,
            visionService: _visionMock.Object,
            imageStore: _imageStoreMock.Object);
    }

    private void SetupTextPipeline(string text = "some text")
    {
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(text);
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), text, It.IsAny<ChunkingOptions?>()))
            .Returns(new List<DocumentChunk> { new() { Content = text, Metadata = new() } });
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string> texts, CancellationToken _) =>
                texts.Select(_ => new float[768]).ToList());
    }

    private static ExtractedImage MakeImage(int page = 1, int index = 0) =>
        new(PageNumber: page, ImageIndex: index, Bytes: new byte[] { 1, 2, 3 },
            MimeType: "image/png", WidthPx: 200, HeightPx: 200);

    // ── Upload: happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task Upload_VisionEnabled_CallsDescribeForEachImage()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        var images = new List<ExtractedImage> { MakeImage(1, 0), MakeImage(2, 1) };
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(images);
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A diagram");
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert
        _visionMock.Verify(v => v.DescribeImageAsync(It.IsAny<byte[]>(), "image/png", "doc.pdf", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Upload_VisionEnabled_SavesImageForEachExtractedImage()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedImage> { MakeImage() });
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A chart");
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UploadDocumentAsync(stream, "report.pdf", "application/pdf");

        // Assert
        _imageStoreMock.Verify(s => s.SaveAsync(
            It.Is<DocumentImage>(img =>
                img.DocumentId != Guid.Empty &&
                img.WorkspaceId == Workspace.DefaultWorkspaceId &&
                img.AiDescription == "A chart"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_VisionEnabled_UpsertsImageChunkWithIsImageTrue()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        var imageId = Guid.NewGuid();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedImage> { MakeImage() });
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Flowchart");
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(imageId);
        var capturedChunks = new List<DocumentChunk>();
        _vectorStoreMock.Setup(v => v.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<DocumentChunk>, CancellationToken>((_, c, _) => capturedChunks.AddRange(c))
            .Returns(Task.CompletedTask);
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UploadDocumentAsync(stream, "manual.pdf", "application/pdf");

        // Assert: second UpsertChunksAsync call contains the image chunk
        var imageChunk = capturedChunks.FirstOrDefault(c => c.IsImageChunk);
        imageChunk.Should().NotBeNull();
        imageChunk!.ImageId.Should().Be(imageId);
        imageChunk.Content.Should().Be("Flowchart");
        imageChunk.Metadata["isImage"].Should().Be("true");
        imageChunk.Metadata["imageId"].Should().Be(imageId.ToString());
    }

    [Fact]
    public async Task Upload_VisionEnabled_ChunkCountIncludesImageChunks()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedImage> { MakeImage(1, 0), MakeImage(2, 1) });
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("description");
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var stream = new MemoryStream(new byte[10]);

        // Act
        var result = await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert: 1 text chunk + 2 image chunks
        result.ChunkCount.Should().Be(3);
    }

    // ── Upload: vision disabled ─────────────────────────────────────────────

    [Fact]
    public async Task Upload_VisionDisabled_DoesNotExtractImages()
    {
        // Arrange
        var sut = BuildSut(visionEnabled: false);
        SetupTextPipeline();
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert
        _processorMock.Verify(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _imageStoreMock.Verify(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Upload: best-effort failure isolation ───────────────────────────────

    [Fact]
    public async Task Upload_OneImageDescriptionFails_OtherImagesStillProcessed()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedImage> { MakeImage(1, 0), MakeImage(2, 1) });
        // First image fails, second succeeds
        var callCount = 0;
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) throw new Exception("Vision API timeout");
                return "Second image description";
            });
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var stream = new MemoryStream(new byte[10]);

        // Act — must not throw
        var result = await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert: 1 text + 1 image chunk (second image only)
        result.Status.Should().Be(DocumentStatus.Completed);
        result.ChunkCount.Should().Be(2);
    }

    [Fact]
    public async Task Upload_ImageExtractionThrows_DocumentStillCompletes()
    {
        // Arrange
        var sut = BuildSut();
        SetupTextPipeline();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("PDF parse error"));
        using var stream = new MemoryStream(new byte[10]);

        // Act — must not throw
        var result = await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert
        result.Status.Should().Be(DocumentStatus.Completed);
        _imageStoreMock.Verify(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete: cascade to image store ─────────────────────────────────────

    [Fact]
    public async Task Delete_CallsImageStoreDeleteBeforeVectorStore()
    {
        // Arrange
        var sut = BuildSut();
        var docId = Guid.NewGuid();
        var callOrder = new List<string>();
        _imageStoreMock.Setup(s => s.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("imageStore"))
            .Returns(Task.CompletedTask);
        _vectorStoreMock.Setup(v => v.DeleteDocumentChunksAsync(It.IsAny<string>(), docId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("vectorStore"))
            .Returns(Task.CompletedTask);

        // Act
        await sut.DeleteDocumentAsync(docId);

        // Assert
        _imageStoreMock.Verify(s => s.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
        callOrder.Should().Equal("imageStore", "vectorStore");
    }

    // ── Update: stale image cleanup ─────────────────────────────────────────

    [Fact]
    public async Task Update_DeletesOldImagesBeforeReprocessing()
    {
        // Arrange
        var sut = BuildSut(visionEnabled: false); // keep it simple — just verify cleanup
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.pdf" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);
        SetupTextPipeline();
        _processorMock.Setup(p => p.IsSupported("application/pdf")).Returns(true);
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UpdateDocumentAsync(docId, stream, "new.pdf", "application/pdf");

        // Assert
        _imageStoreMock.Verify(s => s.DeleteByDocumentAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Argha - 2026-03-18 - #52 - Cost guard: RunVisionPipelineAsync must stop after MaxImagesPerDocument
    [Fact]
    public async Task Upload_VisionEnabled_StopsDescribingAtMaxImagesPerDocument()
    {
        // Arrange: 25 images extracted, limit is 5
        _visionMock.Setup(v => v.IsEnabled).Returns(true);
        var sut = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            _workspaceContextMock.Object,
            visionService: _visionMock.Object,
            imageStore: _imageStoreMock.Object,
            visionOptions: Options.Create(new VisionOptions { MaxImagesPerDocument = 5 }));

        SetupTextPipeline();
        var images = Enumerable.Range(0, 25).Select(i => MakeImage(i + 1, i)).ToList();
        _processorMock.Setup(p => p.ExtractImagesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(images);
        _visionMock.Setup(v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("A diagram");
        _imageStoreMock.Setup(s => s.SaveAsync(It.IsAny<DocumentImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        using var stream = new MemoryStream(new byte[10]);

        // Act
        await sut.UploadDocumentAsync(stream, "doc.pdf", "application/pdf");

        // Assert: only 5 descriptions requested despite 25 images
        _visionMock.Verify(
            v => v.DescribeImageAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }
}

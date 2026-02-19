using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-02-15 - Unit tests for DocumentsController CRUD endpoints (Phase 1.5)
public class DocumentsControllerTests
{
    private readonly Mock<IDocumentProcessor> _processorMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IDocumentRepository> _repositoryMock;
    private readonly DocumentsController _sut;

    public DocumentsControllerTests()
    {
        _processorMock = new Mock<IDocumentProcessor>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _repositoryMock = new Mock<IDocumentRepository>();

        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain", "application/pdf" });

        // Argha - 2026-02-15 - Use real DocumentService with mocked dependencies since it's a concrete class
        var documentService = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<DocumentService>>(),
            _repositoryMock.Object);

        _sut = new DocumentsController(documentService);
    }

    [Fact]
    public async Task Upload_ValidFile_Returns201WithLocation()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "text", Metadata = new Dictionary<string, string>() }
        };
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some content");
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), "Some content", null))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[768] });

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("test.txt");
        fileMock.Setup(f => f.ContentType).Returns("text/plain");
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        // Act
        var result = await _sut.UploadDocument(fileMock.Object, null, CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var dto = createdResult.Value.Should().BeOfType<DocumentDto>().Subject;
        dto.FileName.Should().Be("test.txt");
    }

    [Fact]
    public async Task Upload_NullFile_Returns400()
    {
        // Act
        var result = await _sut.UploadDocument(null!, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        // Arrange
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(0);

        // Act
        var result = await _sut.UploadDocument(fileMock.Object, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetAll_Returns200WithList()
    {
        // Arrange
        var docs = new List<Document>
        {
            new() { FileName = "a.txt", ContentType = "text/plain" },
            new() { FileName = "b.pdf", ContentType = "application/pdf" }
        };
        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(docs);

        // Act
        var result = await _sut.GetAllDocuments();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<List<DocumentDto>>().Subject;
        dtos.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetById_Found_Returns200()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "found.txt", ContentType = "text/plain" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        // Act
        var result = await _sut.GetDocument(docId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<DocumentDto>().Subject;
        dto.Id.Should().Be(docId);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.GetDocument(Guid.NewGuid());

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Delete_Found_Returns204()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "delete.txt", ContentType = "text/plain" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        // Act
        var result = await _sut.DeleteDocument(docId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _vectorStoreMock.Verify(v => v.DeleteDocumentChunksAsync(docId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        // Arrange
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.DeleteDocument(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // Argha - 2026-02-19 - Tag-related controller tests (Phase 2.3)

    [Fact]
    public async Task Upload_WithTags_IncludesTagsInDto()
    {
        // Arrange
        var tags = new List<string> { "finance", "q1" };
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "text", Metadata = new Dictionary<string, string>() }
        };
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some content");
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), "Some content", null))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[768] });

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("tagged.txt");
        fileMock.Setup(f => f.ContentType).Returns("text/plain");
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        // Act
        var result = await _sut.UploadDocument(fileMock.Object, tags, CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = createdResult.Value.Should().BeOfType<DocumentDto>().Subject;
        dto.Tags.Should().BeEquivalentTo(tags);
    }
}

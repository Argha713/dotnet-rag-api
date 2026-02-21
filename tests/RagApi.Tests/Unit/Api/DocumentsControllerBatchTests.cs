// Argha - 2026-02-21 - Unit tests for POST /api/documents/batch (Phase 5.2)
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

namespace RagApi.Tests.Unit.Api;

public class DocumentsControllerBatchTests
{
    private readonly Mock<IDocumentProcessor> _processorMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IDocumentRepository> _repositoryMock;
    private readonly DocumentsController _sut;

    public DocumentsControllerBatchTests()
    {
        _processorMock = new Mock<IDocumentProcessor>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _repositoryMock = new Mock<IDocumentRepository>();

        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes)
            .Returns(new[] { "text/plain", "application/pdf" });

        var documentService = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<DocumentService>>(),
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            Options.Create(new BatchUploadOptions { MaxConcurrency = 2, MaxFilesPerBatch = 5 }));

        _sut = new DocumentsController(
            documentService,
            Options.Create(new BatchUploadOptions { MaxConcurrency = 2, MaxFilesPerBatch = 5 }));
    }

    // ── Input validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task BatchUpload_NoFiles_Returns400()
    {
        var result = await _sut.BatchUploadDocuments(
            new List<IFormFile>(), null, null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BatchUpload_NullFileList_Returns400()
    {
        var result = await _sut.BatchUploadDocuments(
            null!, null, null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BatchUpload_TooManyFiles_Returns400()
    {
        // MaxFilesPerBatch = 5, send 6
        var files = Enumerable.Range(0, 6).Select(_ => MakeFormFile("text/plain")).ToList();

        var result = await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString().Should().Contain("Too many files");
    }

    [Fact]
    public async Task BatchUpload_InvalidChunkingStrategy_Returns400()
    {
        var files = new List<IFormFile> { MakeFormFile("text/plain") };

        var result = await _sut.BatchUploadDocuments(
            files, null, "NotAStrategy", CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString().Should().Contain("NotAStrategy");
    }

    // ── Success paths ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchUpload_AllFilesSucceed_Returns200()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile> { MakeFormFile("text/plain"), MakeFormFile("text/plain") };

        var result = await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task BatchUpload_AllFilesSucceed_SucceededCountMatchesFileCount()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile> { MakeFormFile("text/plain"), MakeFormFile("text/plain") };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.TotalFiles.Should().Be(2);
        dto.Succeeded.Should().Be(2);
        dto.Failed.Should().Be(0);
    }

    [Fact]
    public async Task BatchUpload_AllFilesSucceed_DocumentIdsPopulated()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile> { MakeFormFile("text/plain") };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.Results.Should().AllSatisfy(r => r.DocumentId.Should().NotBeNull());
    }

    [Fact]
    public async Task BatchUpload_AllFilesSucceed_ChunkCountPopulated()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile> { MakeFormFile("text/plain") };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.Results.Should().AllSatisfy(r => r.ChunkCount.Should().BeGreaterThan(0));
    }

    // ── Partial failure ────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchUpload_UnsupportedContentType_MarksFileFailed()
    {
        // Arrange — one supported, one not
        SetupSuccessfulProcessing();
        _processorMock.Setup(p => p.IsSupported("application/zip")).Returns(false);

        var files = new List<IFormFile>
        {
            MakeFormFile("text/plain", "good.txt"),
            MakeFormFile("application/zip", "bad.zip")
        };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.Succeeded.Should().Be(1);
        dto.Failed.Should().Be(1);
        dto.Results.Single(r => r.FileName == "bad.zip").Succeeded.Should().BeFalse();
        dto.Results.Single(r => r.FileName == "bad.zip").ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BatchUpload_PartialFailure_Returns200NotError()
    {
        // Argha - 2026-02-21 - Partial success must still return 200, not 4xx/5xx
        SetupSuccessfulProcessing();
        _processorMock.Setup(p => p.IsSupported("application/zip")).Returns(false);

        var files = new List<IFormFile>
        {
            MakeFormFile("text/plain"),
            MakeFormFile("application/zip")
        };

        var result = await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task BatchUpload_AllFilesFail_Returns200WithAllFailed()
    {
        _processorMock.Setup(p => p.IsSupported(It.IsAny<string>())).Returns(false);

        var files = new List<IFormFile>
        {
            MakeFormFile("application/zip"),
            MakeFormFile("application/zip")
        };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.Succeeded.Should().Be(0);
        dto.Failed.Should().Be(2);
    }

    // ── Shared options propagation ─────────────────────────────────────────────

    [Fact]
    public async Task BatchUpload_ResultCountMatchesFileCount()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile>
        {
            MakeFormFile("text/plain"),
            MakeFormFile("text/plain"),
            MakeFormFile("text/plain")
        };

        var result = (OkObjectResult)await _sut.BatchUploadDocuments(
            files, null, null, CancellationToken.None);

        var dto = result.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.Results.Should().HaveCount(3);
        dto.TotalFiles.Should().Be(3);
    }

    [Fact]
    public async Task BatchUpload_ValidChunkingStrategy_DoesNotReturn400()
    {
        SetupSuccessfulProcessing();
        var files = new List<IFormFile> { MakeFormFile("text/plain") };

        var result = await _sut.BatchUploadDocuments(
            files, null, "Sentence", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetupSuccessfulProcessing()
    {
        var chunks = new List<DocumentChunk>
        {
            new() { Content = "chunk text", Metadata = new Dictionary<string, string>() }
        };

        _processorMock
            .Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some extracted content");

        _processorMock
            .Setup(p => p.ChunkText(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ChunkingOptions?>()))
            .Returns(chunks);

        _embeddingMock
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f, 0.2f } });

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _vectorStoreMock
            .Setup(v => v.UpsertChunksAsync(It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static IFormFile MakeFormFile(string contentType, string fileName = "test.txt")
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[100]));
        return fileMock.Object;
    }
}

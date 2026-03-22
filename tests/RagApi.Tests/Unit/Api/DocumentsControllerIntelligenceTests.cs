// Argha - 2026-03-22 - #83 - Controller tests for Phase 12 endpoints: ingest-url, ingest-zip, summary, tags
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

public class DocumentsControllerIntelligenceTests
{
    private readonly Mock<IDocumentProcessor> _processorMock = new();
    private readonly Mock<IEmbeddingService> _embeddingMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly Mock<IUrlScraper> _scraperMock = new();
    private readonly Mock<IChatService> _chatServiceMock = new();

    private const string TestCollection = "documents";

    public DocumentsControllerIntelligenceTests()
    {
        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain", "application/pdf" });

        // Argha - 2026-03-22 - #83 - Default workspace context returning default workspace
        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = TestCollection
        });
    }

    private DocumentsController BuildController(bool withScraper = false, bool withChat = false)
    {
        var service = new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<DocumentService>>(),
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            _workspaceContextMock.Object,
            urlScraper: withScraper ? _scraperMock.Object : null,
            chatService: withChat ? _chatServiceMock.Object : null);

        return new DocumentsController(service, Options.Create(new BatchUploadOptions()));
    }

    private void SetupSuccessfulUpload(string text = "scraped content")
    {
        var chunks = new List<DocumentChunk>
        {
            new() { Content = text, Metadata = new Dictionary<string, string>() }
        };
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(text);
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), text, It.IsAny<ChunkingOptions?>()))
            .Returns(chunks);
        _embeddingMock.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[768] });
    }

    // ── IngestUrl ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestUrl_ValidRequest_Returns201()
    {
        // Argha - 2026-03-22 - #83 - Successful URL ingestion must return 201 CreatedAtAction with DocumentDto
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload("scraped content");

        _scraperMock.Setup(s => s.ScrapeAsync("https://example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("scraped content", "page.txt"));

        var controller = BuildController(withScraper: true);
        var request = new IngestUrlRequest { Url = "https://example.com" };

        var result = await controller.IngestUrl(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeOfType<DocumentDto>();
    }

    [Fact]
    public async Task IngestUrl_ScraperThrowsNotSupported_Returns415()
    {
        // Argha - 2026-03-22 - #83 - NotSupportedException from IUrlScraper (e.g. YouTube no captions) must return 415
        _scraperMock.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("No captions and audio transcription unavailable"));

        var controller = BuildController(withScraper: true);
        var request = new IngestUrlRequest { Url = "https://www.youtube.com/watch?v=xyz" };

        var result = await controller.IngestUrl(request, CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(415);
    }

    // ── IngestZip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestZip_ValidZip_Returns200WithBatchResult()
    {
        // Argha - 2026-03-22 - #83 - A ZIP with a supported .txt file should produce a 200 BatchUploadResultDto
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload("hello");

        var zipBytes = CreateZipWithTextFile("readme.txt", "hello");
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(zipBytes.Length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(zipBytes));

        var controller = BuildController();
        var result = await controller.IngestZip(fileMock.Object, null, null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var dto = ok.Value.Should().BeOfType<BatchUploadResultDto>().Subject;
        dto.TotalFiles.Should().Be(1);
        dto.Succeeded.Should().Be(1);
    }

    // ── GetDocumentSummary ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentSummary_DocumentExists_Returns200()
    {
        // Argha - 2026-03-22 - #83 - When a document exists and summary is cached, return 200 with DocumentSummaryDto
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "report.txt", SummaryText = "This is a summary." };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var controller = BuildController(withChat: true);
        var result = await controller.GetDocumentSummary(docId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DocumentSummaryDto>().Subject;
        dto.DocumentId.Should().Be(docId);
        dto.Summary.Should().Be("This is a summary.");
    }

    [Fact]
    public async Task GetDocumentSummary_DocumentNotFound_Returns404()
    {
        // Argha - 2026-03-22 - #83 - When the document does not exist, return 404
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var controller = BuildController();
        var result = await controller.GetDocumentSummary(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetTagSuggestions ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagSuggestions_DocumentExists_Returns200()
    {
        // Argha - 2026-03-22 - #83 - When doc exists and tags are cached, return 200 with TagSuggestionsDto
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, SuggestedTagsJson = "[\"tag1\",\"tag2\"]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var controller = BuildController(withChat: true);
        var result = await controller.GetTagSuggestions(docId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TagSuggestionsDto>().Subject;
        dto.DocumentId.Should().Be(docId);
        dto.Tags.Should().BeEquivalentTo(new[] { "tag1", "tag2" });
    }

    [Fact]
    public async Task SetTags_DocumentExists_Returns200()
    {
        // Argha - 2026-03-22 - #83 - SetTags on an existing document must return 200 with updated DocumentDto
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "doc.txt", TagsJson = "[]", SuggestedTagsJson = "[]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = BuildController();
        var request = new SetTagsRequest { Tags = new List<string> { "tax", "legal" } };

        var result = await controller.SetTags(docId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DocumentDto>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Argha - 2026-03-22 - #83 - Build an in-memory ZIP containing one text file
    private static byte[] CreateZipWithTextFile(string entryName, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new System.IO.StreamWriter(entry.Open());
            writer.Write(content);
        }
        return ms.ToArray();
    }
}

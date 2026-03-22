// Argha - 2026-03-22 - #83 - Phase 12 service-layer tests: dedup, versioning, summary, tags, ZIP, URL ingestion
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

public class DocumentIntelligenceServiceTests
{
    private readonly Mock<IDocumentProcessor> _processorMock = new();
    private readonly Mock<IEmbeddingService> _embeddingMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IDocumentRepository> _repositoryMock = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly Mock<IChatService> _chatServiceMock = new();
    private readonly Mock<IUrlScraper> _scraperMock = new();

    private const string TestCollection = "documents";

    public DocumentIntelligenceServiceTests()
    {
        _processorMock.Setup(p => p.IsSupported("text/plain")).Returns(true);
        _processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain", "application/pdf" });

        // Argha - 2026-03-22 - #83 - Default workspace context: default workspace + "documents" collection
        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = TestCollection
        });
    }

    private DocumentService BuildSut(bool withChatService = false, bool withScraper = false)
    {
        return new DocumentService(
            _processorMock.Object,
            _embeddingMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<DocumentService>>(),
            _repositoryMock.Object,
            Options.Create(new DocumentProcessingOptions()),
            _workspaceContextMock.Object,
            urlScraper: withScraper ? _scraperMock.Object : null,
            chatService: withChatService ? _chatServiceMock.Object : null);
    }

    private void SetupSuccessfulUpload(string text = "hello world")
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

    // ── Deduplication ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_DuplicateHash_ReturnsExistingDocument()
    {
        // Argha - 2026-03-22 - #83 - When GetByContentHashAsync returns an existing doc, UploadDocumentAsync must return it with IsDuplicate=true
        var existing = new Document { Id = Guid.NewGuid(), FileName = "original.txt", ContentType = "text/plain" };
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), Workspace.DefaultWorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = BuildSut();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content"));

        var result = await sut.UploadDocumentAsync(stream, "dup.txt", "text/plain");

        result.Id.Should().Be(existing.Id);
        result.IsDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_DuplicateHash_DoesNotCallEmbedding()
    {
        // Argha - 2026-03-22 - #83 - Duplicate detected before embedding: GenerateEmbeddingsAsync must never be called
        var existing = new Document { Id = Guid.NewGuid(), FileName = "original.txt", ContentType = "text/plain" };
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), Workspace.DefaultWorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = BuildSut();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content"));

        await sut.UploadDocumentAsync(stream, "dup.txt", "text/plain");

        _embeddingMock.Verify(e => e.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(v => v.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<List<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadAsync_NewDocument_StoresContentHash()
    {
        // Argha - 2026-03-22 - #83 - When the document is new (no duplicate), AddAsync must be called with a non-null ContentHash
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), Workspace.DefaultWorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload();

        Document? captured = null;
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => captured = d);

        var sut = BuildSut();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));

        await sut.UploadDocumentAsync(stream, "new.txt", "text/plain");

        captured.Should().NotBeNull();
        captured!.ContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadAsync_NonSeekableStream_SkipsDedup()
    {
        // Argha - 2026-03-22 - #83 - Non-seekable stream means hash cannot be computed; dedup is skipped and upload proceeds
        SetupSuccessfulUpload();

        var sut = BuildSut();
        using var inner = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));
        using var nonSeekable = new NonSeekableStream(inner);

        var result = await sut.UploadDocumentAsync(nonSeekable, "ns.txt", "text/plain");

        // Dedup was skipped so GetByContentHashAsync was never called
        _repositoryMock.Verify(r => r.GetByContentHashAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Status.Should().Be(DocumentStatus.Completed);
    }

    // ── Versioning ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDocumentAsync_IncrementsVersion()
    {
        // Argha - 2026-03-22 - #83 - UpdateDocumentAsync must call UpdateAsync with Version incremented by 1
        var docId = Guid.NewGuid();
        var existingDoc = new Document { Id = docId, FileName = "old.txt", Version = 1 };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(existingDoc);
        SetupSuccessfulUpload();

        Document? updated = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => updated = d);

        var sut = BuildSut();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));

        await sut.UpdateDocumentAsync(docId, stream, "new.txt", "text/plain");

        // Argha - 2026-03-22 - #83 - Final UpdateAsync call should have Version == 2
        updated.Should().NotBeNull();
        updated!.Version.Should().Be(2);
    }

    [Fact]
    public async Task UploadAsync_Version_StartsAt1ForNewDocs()
    {
        // Argha - 2026-03-22 - #83 - New docs created by UploadDocumentAsync must have Version == 1
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), Workspace.DefaultWorkspaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload();

        Document? added = null;
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => added = d);

        var sut = BuildSut();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));

        await sut.UploadDocumentAsync(stream, "new.txt", "text/plain");

        added.Should().NotBeNull();
        added!.Version.Should().Be(1);
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentSummaryAsync_CachedSummary_ReturnsCached()
    {
        // Argha - 2026-03-22 - #83 - If SummaryText is already set, return it immediately without calling chat service
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, SummaryText = "existing summary" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetDocumentSummaryAsync(docId);

        result.Should().Be("existing summary");
        _chatServiceMock.Verify(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDocumentSummaryAsync_NoCachedSummary_GeneratesAndCaches()
    {
        // Argha - 2026-03-22 - #83 - When no cached summary, call chat service and persist the result via UpdateAsync
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "report.txt", SummaryText = null };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        var searchResults = new List<SearchResult>
        {
            new() { Content = "chunk content", DocumentId = docId, Score = 0.9 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestCollection, It.IsAny<float[]>(), It.IsAny<int>(), docId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Generated summary text");

        Document? persisted = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => persisted = d);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetDocumentSummaryAsync(docId);

        result.Should().Be("Generated summary text");
        persisted.Should().NotBeNull();
        persisted!.SummaryText.Should().Be("Generated summary text");
    }

    [Fact]
    public async Task GetDocumentSummaryAsync_DocumentNotFound_ReturnsNull()
    {
        // Argha - 2026-03-22 - #83 - Non-existent document should return null without throwing
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetDocumentSummaryAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── Tag suggestions ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTagSuggestionsAsync_CachedTags_ReturnsCached()
    {
        // Argha - 2026-03-22 - #83 - If SuggestedTagsJson already has tags, return them without calling chat service
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, SuggestedTagsJson = "[\"a\",\"b\"]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetTagSuggestionsAsync(docId);

        result.Should().BeEquivalentTo(new[] { "a", "b" });
        _chatServiceMock.Verify(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTagSuggestionsAsync_NoCached_GeneratesAndCaches()
    {
        // Argha - 2026-03-22 - #83 - When no cached suggestions, call chat service and persist the result
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, FileName = "report.txt", SuggestedTagsJson = "[]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        var searchResults = new List<SearchResult>
        {
            new() { Content = "chunk content", DocumentId = docId, Score = 0.9 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestCollection, It.IsAny<float[]>(), It.IsAny<int>(), docId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        _chatServiceMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[\"finance\",\"report\",\"q1\"]");

        Document? persisted = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => persisted = d);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetTagSuggestionsAsync(docId);

        result.Should().BeEquivalentTo(new[] { "finance", "report", "q1" });
        persisted.Should().NotBeNull();
        persisted!.SuggestedTagsJson.Should().Contain("finance");
    }

    [Fact]
    public async Task GetTagSuggestionsAsync_DocumentNotFound_ReturnsEmptyList()
    {
        // Argha - 2026-03-22 - #83 - Non-existent document should return empty list without throwing
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        var sut = BuildSut(withChatService: true);
        var result = await sut.GetTagSuggestionsAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    // ── SetTags ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetTagsAsync_UpdatesTagsJson()
    {
        // Argha - 2026-03-22 - #83 - SetTagsAsync must persist the new tags as JSON
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, TagsJson = "[]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        Document? updated = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => updated = d);

        var sut = BuildSut();
        await sut.SetTagsAsync(docId, new List<string> { "tax", "2025" });

        updated.Should().NotBeNull();
        updated!.TagsJson.Should().Contain("tax").And.Contain("2025");
    }

    [Fact]
    public async Task SetTagsAsync_ResetsSuggestedTagsJson()
    {
        // Argha - 2026-03-22 - #83 - After setting tags, SuggestedTagsJson must be reset to "[]" so the next call regenerates
        var docId = Guid.NewGuid();
        var doc = new Document { Id = docId, SuggestedTagsJson = "[\"old\",\"cached\"]" };
        _repositoryMock.Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        Document? updated = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => updated = d);

        var sut = BuildSut();
        await sut.SetTagsAsync(docId, new List<string> { "new" });

        updated.Should().NotBeNull();
        updated!.SuggestedTagsJson.Should().Be("[]");
    }

    // ── IngestZip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestZipAsync_ValidZip_ProcessesSupportedFiles()
    {
        // Argha - 2026-03-22 - #83 - ZIP with one .txt file should produce one batch result
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload();

        var zipStream = CreateZipWithTextFile("readme.txt", "hello world content");

        var sut = BuildSut();
        var results = await sut.IngestZipAsync(zipStream);

        results.Should().HaveCount(1);
        results[0].FileName.Should().Be("readme.txt");
        results[0].Succeeded.Should().BeTrue();
    }

    // ── IngestUrl ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestUrlAsync_DelegatesScraperResult_ToUploadPipeline()
    {
        // Argha - 2026-03-22 - #83 - IUrlScraper returns text; DocumentService feeds it through standard upload pipeline
        _repositoryMock.Setup(r => r.GetByContentHashAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);
        SetupSuccessfulUpload("scraped content");

        _scraperMock.Setup(s => s.ScrapeAsync("https://example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("scraped content", "page.txt"));

        // Argha - 2026-03-22 - #83 - ChunkText is called with the scraped text
        _processorMock.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync("scraped content");
        _processorMock.Setup(p => p.ChunkText(It.IsAny<Guid>(), "scraped content", It.IsAny<ChunkingOptions?>()))
            .Returns(new List<DocumentChunk> { new() { Content = "scraped content", Metadata = new() } });

        var sut = BuildSut(withScraper: true);
        var result = await sut.IngestUrlAsync("https://example.com");

        result.FileName.Should().Be("page.txt");
        result.Status.Should().Be(DocumentStatus.Completed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Argha - 2026-03-22 - #83 - Build an in-memory ZIP containing a single text file
    private static MemoryStream CreateZipWithTextFile(string entryName, string content)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new System.IO.StreamWriter(entry.Open());
            writer.Write(content);
        }
        ms.Position = 0;
        return ms;
    }
}

// Argha - 2026-03-22 - #83 - Stream that delegates to an inner stream but returns CanSeek=false
// Length returns 0 because DocumentService accesses it for FileSize even when CanSeek=false
internal sealed class NonSeekableStream : Stream
{
    private readonly Stream _inner;
    public NonSeekableStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 0; // Argha - 2026-03-22 - #83 - Return 0 so FileSize assignment doesn't throw
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

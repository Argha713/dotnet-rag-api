using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-03-22 - #65 - Unit tests for AgentTools tool implementations
public class AgentToolsTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IChatService> _chatMock;
    private readonly AgentTools _sut;

    private static readonly float[] TestEmbedding = new float[768];

    public AgentToolsTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _chatMock = new Mock<IChatService>();
        _chatMock.Setup(c => c.ModelName).Returns("test-model");

        var embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEmbedding);

        var workspaceContext = new Mock<IWorkspaceContext>();
        workspaceContext.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = "documents"
        });

        var ragService = new RagService(
            _vectorStoreMock.Object,
            embeddingMock.Object,
            _chatMock.Object,
            Mock.Of<ILogger<RagService>>(),
            Options.Create(new SearchOptions()),
            workspaceContext.Object);

        _sut = new AgentTools(ragService, _chatMock.Object, Mock.Of<ILogger<AgentTools>>());
    }

    // ───── search_documents ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SearchDocuments_ReturnsFormattedResults()
    {
        _vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "report.pdf", Content = "Revenue was $10M.", Score = 0.95 }
            });

        var result = await _sut.ExecuteAsync("search_documents", "revenue", CancellationToken.None);

        result.Text.Should().Contain("report.pdf");
        result.Text.Should().Contain("Revenue was $10M.");
        result.Sources.Should().HaveCount(1);
        result.Sources[0].FileName.Should().Be("report.pdf");
    }

    [Fact]
    public async Task ExecuteAsync_SearchDocuments_NoResults_ReturnsNotFoundMessage()
    {
        _vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        var result = await _sut.ExecuteAsync("search_documents", "nonexistent topic", CancellationToken.None);

        result.Text.Should().Contain("No relevant documents found");
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SearchDocuments_ReturnsSourceCitationsForAllChunks()
    {
        var docId = Guid.NewGuid();
        _vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), DocumentId = docId, FileName = "a.txt", Content = "chunk A", Score = 0.9, ChunkIndex = 0 },
                new() { ChunkId = Guid.NewGuid(), DocumentId = docId, FileName = "a.txt", Content = "chunk B", Score = 0.8, ChunkIndex = 1 }
            });

        var result = await _sut.ExecuteAsync("search_documents", "query", CancellationToken.None);

        result.Sources.Should().HaveCount(2);
    }

    // ───── get_document_summary ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_GetDocumentSummary_CallsChatServiceWithChunks()
    {
        _vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), TestEmbedding, It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>
            {
                new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "manual.pdf", Content = "Chapter 1 content.", Score = 0.9 }
            });

        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This document covers manual procedures.");

        var result = await _sut.ExecuteAsync("get_document_summary", "manual.pdf", CancellationToken.None);

        result.Text.Should().Contain("manual.pdf");
        result.Text.Should().Contain("This document covers manual procedures.");
    }

    [Fact]
    public async Task ExecuteAsync_GetDocumentSummary_NoResults_ReturnsNotFoundMessage()
    {
        _vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), TestEmbedding, It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        var result = await _sut.ExecuteAsync("get_document_summary", "ghost.pdf", CancellationToken.None);

        result.Text.Should().Contain("No document found");
    }

    // ───── compare_chunks ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CompareChunks_ValidJson_CallsChatService()
    {
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Text1 and Text2 agree on the main point.");

        var input = """{"text1":"The price increased.","text2":"The price went up."}""";
        var result = await _sut.ExecuteAsync("compare_chunks", input, CancellationToken.None);

        result.Text.Should().Be("Text1 and Text2 agree on the main point.");
        _chatMock.Verify(c => c.GenerateResponseAsync(
            It.IsAny<string>(),
            It.Is<List<ChatMessage>>(m => m.Any(msg => msg.Content.Contains("The price increased."))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CompareChunks_InvalidJson_FallsBackToText1Only()
    {
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Analysis complete.");

        var result = await _sut.ExecuteAsync("compare_chunks", "not valid json", CancellationToken.None);

        result.Text.Should().Be("Analysis complete.");
    }

    // ───── unknown tool ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorMessage()
    {
        var result = await _sut.ExecuteAsync("fly_to_moon", "anywhere", CancellationToken.None);

        result.Text.Should().Contain("Unknown tool");
        result.Text.Should().Contain("fly_to_moon");
    }
}

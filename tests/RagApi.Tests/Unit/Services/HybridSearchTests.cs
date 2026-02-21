using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-20 - Unit tests for hybrid search (RRF fusion) in RagService 
public class HybridSearchTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingServiceMock;
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;

    private static readonly float[] TestEmbedding = new float[768];

    public HybridSearchTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingServiceMock = new Mock<IEmbeddingService>();
        _chatServiceMock = new Mock<IChatService>();
        _loggerMock = new Mock<ILogger<RagService>>();

        _embeddingServiceMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEmbedding);
        _chatServiceMock.Setup(c => c.ModelName).Returns("llama3.2");
    }

    private RagService CreateSut(SearchOptions? options = null)
    {
        return new RagService(
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _chatServiceMock.Object,
            _loggerMock.Object,
            Options.Create(options ?? new SearchOptions()));
    }

    [Fact]
    public async Task SearchAsync_HybridEnabled_CallsBothSearches()
    {
        // Arrange
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 2 });
        var semanticResults = MakeResults(2, startScore: 0.9);
        var keywordResults = MakeResults(2, startScore: 1.0);

        _vectorStoreMock
            .Setup(v => v.SearchAsync(TestEmbedding, 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(semanticResults);
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync("test query", 10, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(keywordResults);

        // Act
        await sut.SearchAsync("test query", topK: 5);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 10, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.KeywordSearchAsync("test query", 10, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HybridDisabled_CallsOnlySemanticSearch()
    {
        // Arrange — UseHybridSearch defaults to false
        var sut = CreateSut();
        _vectorStoreMock
            .Setup(v => v.SearchAsync(TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResults(2));

        // Act
        await sut.SearchAsync("test query");

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_PerRequestOverride_TrueEnablesHybridEvenWhenConfigFalse()
    {
        // Arrange — config = false, request override = true
        var sut = CreateSut(new SearchOptions { UseHybridSearch = false, CandidateMultiplier = 1 });
        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        await sut.SearchAsync("query", useHybridSearch: true);

        // Assert
        _vectorStoreMock.Verify(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_PerRequestOverride_FalseDisablesHybridEvenWhenConfigTrue()
    {
        // Arrange — config = true, request override = false
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true });
        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResults(2));

        // Act
        await sut.SearchAsync("query", useHybridSearch: false);

        // Assert
        _vectorStoreMock.Verify(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_HybridFusion_DeduplicatesChunkInBothLists()
    {
        // Arrange — same chunk appears in both semantic and keyword results
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 1 });
        var sharedChunk = new SearchResult { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "a.txt", Content = "shared", Score = 0.9, ChunkIndex = 0 };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult> { sharedChunk });
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult> { sharedChunk });

        // Act
        var results = await sut.SearchAsync("query", topK: 5);

        // Assert — deduplicated; only one instance of the shared chunk
        results.Should().HaveCount(1);
        results[0].ChunkId.Should().Be(sharedChunk.ChunkId);
    }

    [Fact]
    public async Task SearchAsync_HybridFusion_ResultsRankedByRrfScore()
    {
        // Arrange — chunkA rank-1 in semantic, chunkB rank-1 in keyword, chunkC rank-2 in both
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 1 });

        var chunkA = MakeResult("chunkA", score: 0.95); // rank 0 in semantic
        var chunkB = MakeResult("chunkB", score: 0.85); // rank 1 in semantic, rank 0 in keyword
        var chunkC = MakeResult("chunkC", score: 0.75); // rank 2 in semantic

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult> { chunkA, chunkB, chunkC });
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult> { chunkB }); // chunkB in both lists → boosted

        // Act
        var results = await sut.SearchAsync("query", topK: 3);

        // Assert — chunkB appears in both lists so its RRF score should be highest
        results.Should().HaveCount(3);
        results[0].ChunkId.Should().Be(chunkB.ChunkId);
    }

    [Fact]
    public async Task SearchAsync_HybridFiltersPassedToBothSearches()
    {
        // Arrange
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 1 });
        var docId = Guid.NewGuid();
        var tags = new List<string> { "finance" };

        _vectorStoreMock
            .Setup(v => v.SearchAsync(TestEmbedding, It.IsAny<int>(), docId, tags, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync("q", It.IsAny<int>(), docId, tags, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        await sut.SearchAsync("q", filterByDocumentId: docId, filterByTags: tags);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, It.IsAny<int>(), docId, tags, It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.KeywordSearchAsync("q", It.IsAny<int>(), docId, tags, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HybridBothEmpty_ReturnsEmpty()
    {
        // Arrange
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 1 });

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var results = await sut.SearchAsync("nothing matches");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_HybridTrimmedToTopK()
    {
        // Arrange
        var sut = CreateSut(new SearchOptions { UseHybridSearch = true, CandidateMultiplier = 1 });
        var many = MakeResults(10);

        _vectorStoreMock
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(many);
        _vectorStoreMock
            .Setup(v => v.KeywordSearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        // Act
        var results = await sut.SearchAsync("query", topK: 3);

        // Assert
        results.Should().HaveCount(3);
    }

    // --- helpers ---

    private static SearchResult MakeResult(string fileName, double score = 0.8)
        => new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = fileName,
            Content = $"content of {fileName}",
            Score = score,
            ChunkIndex = 0
        };

    private static List<SearchResult> MakeResults(int count, double startScore = 0.9)
        => Enumerable.Range(0, count).Select(i => new SearchResult
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = $"doc{i}.txt",
            Content = $"Content {i}",
            Score = startScore - (i * 0.05),
            ChunkIndex = i
        }).ToList();
}

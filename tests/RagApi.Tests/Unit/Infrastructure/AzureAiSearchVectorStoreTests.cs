// Argha - 2026-02-21 - Unit tests for AzureAiSearchVectorStore and AzureAiSearchHealthCheck 
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Domain.Entities;
using RagApi.Infrastructure;
using RagApi.Infrastructure.HealthChecks;
using RagApi.Infrastructure.VectorStore;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-02-21 - Azure SDK clients (SearchIndexClient, SearchClient) are non-sealed and mockable with Moq.
// Moq requires .Returns(Task.FromResult(...)) for Azure.Response<T> return types (ReturnsAsync has type inference issues).
// Azure SDK factory types are created via SearchModelFactory (not constructors).
public class AzureAiSearchVectorStoreTests
{
    private readonly Mock<SearchIndexClient> _indexClientMock;
    private readonly Mock<SearchClient> _searchClientMock;
    private readonly IOptions<VectorStoreConfiguration> _config;
    private readonly Mock<ILogger<AzureAiSearchVectorStore>> _loggerMock;
    private readonly AzureAiSearchVectorStore _sut;

    public AzureAiSearchVectorStoreTests()
    {
        _indexClientMock = new Mock<SearchIndexClient>();
        _searchClientMock = new Mock<SearchClient>();
        _loggerMock = new Mock<ILogger<AzureAiSearchVectorStore>>();

        _config = Options.Create(new VectorStoreConfiguration
        {
            Provider = "AzureAiSearch",
            AzureAiSearch = new AzureAiSearchSettings
            {
                Endpoint = "https://test.search.windows.net",
                ApiKey = "test-key",
                IndexName = "test-index",
                EmbeddingDimension = 3
            }
        });

        _sut = new AzureAiSearchVectorStore(
            _indexClientMock.Object,
            _searchClientMock.Object,
            _config,
            _loggerMock.Object);
    }

    // ── InitializeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_CallsCreateOrUpdateIndex()
    {
        // Arrange
        _indexClientMock
            .Setup(c => c.CreateOrUpdateIndexAsync(
                It.IsAny<SearchIndex>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(
                Response.FromValue(new SearchIndex("test-index"), Mock.Of<Response>())));

        // Act
        await _sut.InitializeAsync();

        // Assert
        _indexClientMock.Verify(c => c.CreateOrUpdateIndexAsync(
            It.Is<SearchIndex>(i => i.Name == "test-index"),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_IndexSchemaIncludesRequiredFields()
    {
        // Arrange
        SearchIndex? capturedIndex = null;

        _indexClientMock
            .Setup(c => c.CreateOrUpdateIndexAsync(
                It.IsAny<SearchIndex>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<SearchIndex, bool, bool, CancellationToken>(
                (idx, _, _, _) => capturedIndex = idx)
            .Returns(Task.FromResult(
                Response.FromValue(new SearchIndex("test-index"), Mock.Of<Response>())));

        // Act
        await _sut.InitializeAsync();

        // Assert
        capturedIndex.Should().NotBeNull();
        capturedIndex!.Fields.Should().Contain(f => f.Name == "embedding");
        capturedIndex.Fields.Should().Contain(f => f.Name == "documentId");
        capturedIndex.Fields.Should().Contain(f => f.Name == "content");
        capturedIndex.Fields.Should().Contain(f => f.Name == "tags");
    }

    // ── UpsertChunksAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertChunksAsync_EmptyList_DoesNotCallSearchClient()
    {
        // Act
        await _sut.UpsertChunksAsync(new List<DocumentChunk>());

        // Assert
        _searchClientMock.Verify(
            c => c.IndexDocumentsAsync(
                It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpsertChunksAsync_CallsMergeOrUploadForEachChunk()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = "hello",
                    Embedding = new float[] { 0.1f, 0.2f, 0.3f },
                    Metadata = new Dictionary<string, string>
                    {
                        ["fileName"] = "test.txt",
                        ["contentType"] = "text/plain"
                    } }
        };

        // Argha - 2026-02-21 - IndexDocumentsResult created via SearchModelFactory (sealed internal ctor)
        var indexResult = SearchModelFactory.IndexDocumentsResult(new[]
        {
            SearchModelFactory.IndexingResult(chunks[0].Id.ToString(), errorMessage: null, succeeded: true, status: 200)
        });

        _searchClientMock
            .Setup(c => c.IndexDocumentsAsync(
                It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Response.FromValue(indexResult, Mock.Of<Response>())));

        // Act
        await _sut.UpsertChunksAsync(chunks);

        // Assert
        _searchClientMock.Verify(
            c => c.IndexDocumentsAsync(
                It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GetStatsAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsMappedStats()
    {
        // Arrange
        var fakeStats = SearchModelFactory.SearchIndexStatistics(documentCount: 42, storageSize: 1000);

        _indexClientMock
            .Setup(c => c.GetIndexStatisticsAsync("test-index", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Response.FromValue(fakeStats, Mock.Of<Response>())));

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        stats.CollectionName.Should().Be("test-index");
        stats.TotalVectors.Should().Be(42);
        stats.VectorDimension.Should().Be(3);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectIndexName()
    {
        // Arrange
        var fakeStats = SearchModelFactory.SearchIndexStatistics(documentCount: 0, storageSize: 0);

        _indexClientMock
            .Setup(c => c.GetIndexStatisticsAsync("test-index", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Response.FromValue(fakeStats, Mock.Of<Response>())));

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        stats.CollectionName.Should().Be("test-index");
    }

    // ── SearchAsync (vector) ───────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        SetupEmptySearchResponse();

        // Act
        var results = await _sut.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_PassesTopKToQuery()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f }, topK: 7);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Size.Should().Be(7);
    }

    [Fact]
    public async Task SearchAsync_WithDocumentFilter_SetsODataFilter()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);
        var docId = Guid.NewGuid();

        // Act
        await _sut.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f }, filterByDocumentId: docId);

        // Assert
        capturedOptions!.Filter.Should().Contain(docId.ToString());
        capturedOptions.Filter.Should().Contain("documentId eq");
    }

    [Fact]
    public async Task SearchAsync_WithTagFilter_SetsODataFilter()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f }, filterByTags: ["finance"]);

        // Assert
        capturedOptions!.Filter.Should().Contain("tags/any");
        capturedOptions.Filter.Should().Contain("finance");
    }

    [Fact]
    public async Task SearchAsync_WithBothFilters_CombinesWithAnd()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);
        var docId = Guid.NewGuid();

        // Act
        await _sut.SearchAsync(
            new float[] { 0.1f, 0.2f, 0.3f },
            filterByDocumentId: docId,
            filterByTags: ["hr"]);

        // Assert
        capturedOptions!.Filter.Should().Contain(" and ");
    }

    [Fact]
    public async Task SearchAsync_NoFilters_FilterIsNull()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.SearchAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Assert
        capturedOptions!.Filter.Should().BeNullOrEmpty();
    }

    // ── SearchWithEmbeddingsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SearchWithEmbeddingsAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        SetupEmptySearchResponse();

        // Act
        var results = await _sut.SearchWithEmbeddingsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchWithEmbeddingsAsync_SelectsEmbeddingField()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.SearchWithEmbeddingsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        // Assert
        capturedOptions!.Select.Should().Contain("embedding");
    }

    // ── KeywordSearchAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task KeywordSearchAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        SetupEmptySearchResponse();

        // Act
        var results = await _sut.KeywordSearchAsync("test query");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task KeywordSearchAsync_WithTagFilter_SetsODataFilter()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.KeywordSearchAsync("invoice", filterByTags: ["legal"]);

        // Assert
        capturedOptions!.Filter.Should().Contain("tags/any");
        capturedOptions.Filter.Should().Contain("legal");
    }

    [Fact]
    public async Task KeywordSearchAsync_DoesNotAddVectorQuery()
    {
        // Arrange
        SearchOptions? capturedOptions = null;
        SetupEmptySearchResponse(captureOptions: opts => capturedOptions = opts);

        // Act
        await _sut.KeywordSearchAsync("test query");

        // Assert
        // Argha - 2026-02-21 - keyword search must not include a VectorSearch query (BM25 only)
        capturedOptions!.VectorSearch.Should().BeNull();
    }

    // ── DeleteDocumentChunksAsync ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteDocumentChunksAsync_NoChunksFound_DoesNotCallDelete()
    {
        // Arrange
        SetupEmptySearchResponse();

        // Act
        await _sut.DeleteDocumentChunksAsync(Guid.NewGuid());

        // Assert — IndexDocumentsAsync must not be called if nothing was found
        _searchClientMock.Verify(
            c => c.IndexDocumentsAsync(
                It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AzureAiSearchHealthCheck ───────────────────────────────────────────────

    [Fact]
    public async Task AzureAiSearchHealthCheck_Healthy_WhenIndexReachable()
    {
        // Arrange
        var fakeStats = SearchModelFactory.SearchIndexStatistics(documentCount: 0, storageSize: 0);

        _indexClientMock
            .Setup(c => c.GetIndexStatisticsAsync("test-index", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Response.FromValue(fakeStats, Mock.Of<Response>())));

        var healthCheck = new AzureAiSearchHealthCheck(
            _indexClientMock.Object,
            _config,
            Mock.Of<ILogger<AzureAiSearchHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Azure AI Search reachable");
        result.Description.Should().Contain("test-index");
    }

    [Fact]
    public async Task AzureAiSearchHealthCheck_Unhealthy_WhenException()
    {
        // Arrange
        _indexClientMock
            .Setup(c => c.GetIndexStatisticsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException("Service unavailable"));

        var healthCheck = new AzureAiSearchHealthCheck(
            _indexClientMock.Object,
            _config,
            Mock.Of<ILogger<AzureAiSearchHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("unreachable");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void SetupEmptySearchResponse(Action<SearchOptions>? captureOptions = null)
    {
        // Argha - 2026-02-21 - SearchModelFactory creates a SearchResults<T> with zero results for mocking
        var emptyResults = SearchModelFactory.SearchResults<SearchDocument>(
            values: Array.Empty<Azure.Search.Documents.Models.SearchResult<SearchDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: Mock.Of<Response>());

        _searchClientMock
            .Setup(c => c.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>(
                (_, opts, _) => captureOptions?.Invoke(opts))
            .Returns(Task.FromResult(
                Response.FromValue(emptyResults, Mock.Of<Response>())));
    }
}

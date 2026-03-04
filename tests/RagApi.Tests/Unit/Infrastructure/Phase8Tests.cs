// Argha - 2026-03-02 - #5 #6 - Phase 8: PostgreSQL health check + Qdrant resilience tests
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Qdrant.Client.Grpc;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using RagApi.Infrastructure;
using RagApi.Infrastructure.Data;
using RagApi.Infrastructure.HealthChecks;
using RagApi.Infrastructure.VectorStore;

namespace RagApi.Tests.Unit.Infrastructure;

// ── PostgresHealthCheck (2 tests) ────────────────────────────────────────────

public class PostgresHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_CanConnect_ReturnsHealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new RagApiDbContext(options);
        var healthCheck = new PostgresHealthCheck(dbContext, Mock.Of<ILogger<PostgresHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("PostgreSQL database reachable");
    }

    [Fact]
    public async Task CheckHealthAsync_DisposedContext_ReturnsUnhealthy()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dbContext = new RagApiDbContext(options);
        dbContext.Dispose(); // Argha - 2026-03-02 - #6 - Force disposal so CanConnectAsync throws

        var healthCheck = new PostgresHealthCheck(dbContext, Mock.Of<ILogger<PostgresHealthCheck>>());

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}

// ── QdrantVectorStore resilience (8 tests) ───────────────────────────────────

public class QdrantVectorStoreResilientTests
{
    private readonly Mock<IQdrantClient> _mockClient;
    private readonly QdrantVectorStore _sut;

    public QdrantVectorStoreResilientTests()
    {
        _mockClient = new Mock<IQdrantClient>();

        var mockEmbeddingService = new Mock<IEmbeddingService>();
        mockEmbeddingService.Setup(e => e.EmbeddingDimension).Returns(768);

        var config = Options.Create(new QdrantConfiguration
        {
            CollectionName = "documents",
            Host = "localhost",
            Port = 6334
        });

        // Argha - 2026-03-02 - #5 - Uses internal constructor exposed via InternalsVisibleTo
        _sut = new QdrantVectorStore(
            _mockClient.Object,
            config,
            mockEmbeddingService.Object,
            Mock.Of<ILogger<QdrantVectorStore>>());
    }

    // Argha - 2026-03-02 - #5 - Helper: set up EnsureCollectionAsync to succeed (collection exists)
    private void SetupEnsureCollectionExists()
    {
        _mockClient.Setup(c => c.ListCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "documents" });
        _mockClient.Setup(c => c.CreatePayloadIndexAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PayloadSchemaType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // Argha - 2026-03-02 - #5 - Helper: set up EnsureCollectionAsync to create the collection (missing)
    private void SetupEnsureCollectionMissing()
    {
        _mockClient.Setup(c => c.ListCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockClient.Setup(c => c.CreateCollectionAsync(
                It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockClient.Setup(c => c.CreatePayloadIndexAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PayloadSchemaType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── EnsureCollectionAsync tests ──────────────────────────────────────────

    [Fact]
    public async Task EnsureCollectionAsync_CollectionExists_DoesNotCreateCollection()
    {
        // Arrange
        SetupEnsureCollectionExists();

        // Act
        // Argha - 2026-03-04 - #17 - InitializeAsync replaced by EnsureCollectionAsync(collectionName)
        await _sut.EnsureCollectionAsync("documents");

        // Assert — collection already exists so CreateCollectionAsync must not be called
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Never());
        _mockClient.Verify(c => c.CreatePayloadIndexAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PayloadSchemaType>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task EnsureCollectionAsync_CollectionMissing_CreatesCollection()
    {
        // Arrange
        SetupEnsureCollectionMissing();

        // Act
        // Argha - 2026-03-04 - #17 - InitializeAsync replaced by EnsureCollectionAsync(collectionName)
        await _sut.EnsureCollectionAsync("documents");

        // Assert — missing collection triggers one CreateCollectionAsync call
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        _mockClient.Verify(c => c.CreatePayloadIndexAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PayloadSchemaType>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    // ── ExecuteWithReinitAsync resilience tests ──────────────────────────────

    [Fact]
    public async Task SearchAsync_CollectionNotFound_ReInitializesAndRetries()
    {
        // Arrange — first call throws NotFound; reinit finds empty collection list; second call succeeds
        _mockClient.SetupSequence(c => c.SearchAsync(
                It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<ulong>(),
                It.IsAny<Filter?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "collection not found")))
            .ReturnsAsync(new List<ScoredPoint>());
        SetupEnsureCollectionMissing();

        // Act
        // Argha - 2026-03-04 - #17 - SearchAsync now takes collectionName as first param
        var results = await _sut.SearchAsync("documents", new float[] { 0.1f }, topK: 5);

        // Assert — reinit ran (CreateCollectionAsync called once) and retry returned empty results
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NonNotFoundRpcException_DoesNotReinit_Rethrows()
    {
        // Arrange — Unavailable is a non-retriable error; reinit must NOT run
        _mockClient.Setup(c => c.SearchAsync(
                It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<ulong>(),
                It.IsAny<Filter?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "service unavailable")));

        // Act
        // Argha - 2026-03-04 - #17 - SearchAsync now takes collectionName as first param
        var act = async () => await _sut.SearchAsync("documents", new float[] { 0.1f }, topK: 5);

        // Assert — exception propagates and no reinit attempt was made
        await act.Should().ThrowAsync<RpcException>()
            .Where(ex => ex.StatusCode == StatusCode.Unavailable);
        _mockClient.Verify(c => c.ListCollectionsAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task UpsertChunksAsync_CollectionNotFound_ReInitializesAndRetries()
    {
        // Arrange
        _mockClient.SetupSequence(c => c.UpsertAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<PointStruct>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "collection not found")))
            .Returns(Task.CompletedTask);
        SetupEnsureCollectionMissing();

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Content = "test content",
            Embedding = new float[] { 0.1f, 0.2f },
            Metadata = new Dictionary<string, string>
            {
                ["fileName"] = "test.txt",
                ["contentType"] = "text/plain"
            }
        };

        // Act
        // Argha - 2026-03-04 - #17 - UpsertChunksAsync now takes collectionName as first param
        await _sut.UpsertChunksAsync("documents", new List<DocumentChunk> { chunk });

        // Assert — reinit + retry succeeded
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<PointStruct>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteDocumentChunksAsync_CollectionNotFound_ReInitializesAndRetries()
    {
        // Arrange
        _mockClient.SetupSequence(c => c.DeleteAsync(
                It.IsAny<string>(), It.IsAny<Filter>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "collection not found")))
            .Returns(Task.CompletedTask);
        SetupEnsureCollectionMissing();

        // Act
        // Argha - 2026-03-04 - #17 - DeleteDocumentChunksAsync now takes collectionName as first param
        await _sut.DeleteDocumentChunksAsync("documents", Guid.NewGuid());

        // Assert
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        _mockClient.Verify(c => c.DeleteAsync(
            It.IsAny<string>(), It.IsAny<Filter>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetStatsAsync_CollectionNotFound_ReInitializesAndRetries()
    {
        // Arrange
        _mockClient.SetupSequence(c => c.GetCollectionInfoAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "collection not found")))
            .ReturnsAsync(new CollectionInfo());
        SetupEnsureCollectionMissing();

        // Act
        // Argha - 2026-03-04 - #17 - GetStatsAsync now takes collectionName as first param
        var stats = await _sut.GetStatsAsync("documents");

        // Assert
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        stats.Should().NotBeNull();
        stats.TotalVectors.Should().Be(0);
    }

    [Fact]
    public async Task KeywordSearchAsync_CollectionNotFound_ReInitializesAndRetries()
    {
        // Arrange
        _mockClient.SetupSequence(c => c.ScrollAsync(
                It.IsAny<string>(), It.IsAny<Filter?>(), It.IsAny<uint>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.NotFound, "collection not found")))
            .ReturnsAsync(new ScrollResponse());
        SetupEnsureCollectionMissing();

        // Act
        // Argha - 2026-03-04 - #17 - KeywordSearchAsync now takes collectionName as first param
        var results = await _sut.KeywordSearchAsync("documents", "test query", topK: 5);

        // Assert
        _mockClient.Verify(c => c.CreateCollectionAsync(
            It.IsAny<string>(), It.IsAny<VectorParams>(), It.IsAny<CancellationToken>()), Times.Once());
        results.Should().BeEmpty();
    }
}

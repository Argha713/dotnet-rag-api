using Grpc.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Infrastructure.VectorStore;

/// <summary>
/// Vector store implementation using Qdrant
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly IQdrantClient _client;
    private readonly QdrantConfiguration _config;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QdrantVectorStore> _logger;
    // Argha - 2026-03-02 - #5 - Guards EnsureCollectionAsync so concurrent NotFound errors trigger only one reinit
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Argha - 2026-03-02 - #5 - Public constructor used by DI; wraps real QdrantClient in adapter
    public QdrantVectorStore(
        IOptions<QdrantConfiguration> config,
        IEmbeddingService embeddingService,
        ILogger<QdrantVectorStore> logger)
        : this(
            new QdrantClientAdapter(new QdrantClient(
                host: config.Value.Host,
                port: config.Value.Port,
                https: config.Value.UseTls,
                apiKey: config.Value.ApiKey)),
            config,
            embeddingService,
            logger)
    { }

    // Argha - 2026-03-02 - #5 - Internal constructor used by unit tests via InternalsVisibleTo
    internal QdrantVectorStore(
        IQdrantClient client,
        IOptions<QdrantConfiguration> config,
        IEmbeddingService embeddingService,
        ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _config = config.Value;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureCollectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant collection");
            throw;
        }
    }

    // Argha - 2026-03-02 - #5 - Idempotent: checks collection exists before creating; called on startup and on NotFound recovery
    private async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        var collections = await _client.ListCollectionsAsync(cancellationToken);

        if (!collections.Any(c => c == _config.CollectionName))
        {
            _logger.LogInformation("Creating Qdrant collection: {CollectionName}", _config.CollectionName);

            await _client.CreateCollectionAsync(
                name: _config.CollectionName,
                vectorParams: new VectorParams
                {
                    Size = (ulong)_embeddingService.EmbeddingDimension,
                    Distance = Distance.Cosine
                },
                ct: cancellationToken);

            _logger.LogInformation("Collection created successfully");
        }
        else
        {
            _logger.LogDebug("Collection {CollectionName} already exists", _config.CollectionName);
        }

        // Argha - 2026-02-20 - Create full-text payload index on 'content' for keyword search
        // CreatePayloadIndexAsync is idempotent — safe to call on every startup
        await _client.CreatePayloadIndexAsync(
            name: _config.CollectionName,
            field: "content",
            schema: PayloadSchemaType.Text,
            ct: cancellationToken);
        _logger.LogDebug("Full-text payload index on 'content' ensured");
    }

    // Argha - 2026-03-02 - #5 - Catches NotFound, reinitializes collection, retries once; covers all public methods
    private async Task<T> ExecuteWithReinitAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Qdrant collection '{Collection}' not found — reinitializing and retrying", _config.CollectionName);
            await _initLock.WaitAsync(ct);
            try
            {
                await EnsureCollectionAsync(ct);
            }
            finally
            {
                _initLock.Release();
            }
            return await operation(ct);
        }
    }

    // Argha - 2026-03-02 - #5 - Void overload of ExecuteWithReinitAsync for methods that return Task
    private async Task ExecuteWithReinitAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Qdrant collection '{Collection}' not found — reinitializing and retrying", _config.CollectionName);
            await _initLock.WaitAsync(ct);
            try
            {
                await EnsureCollectionAsync(ct);
            }
            finally
            {
                _initLock.Release();
            }
            await operation(ct);
        }
    }

    public async Task UpsertChunksAsync(List<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0) return;

        var points = chunks.Select(chunk =>
        {
            // Argha - 2026-02-19 - Build tags ListValue for Qdrant payload
            var tagsValue = new Value { ListValue = new ListValue() };
            foreach (var tag in chunk.Tags)
                tagsValue.ListValue.Values.Add(new Value { StringValue = tag });

            return new PointStruct
            {
                Id = new PointId { Uuid = chunk.Id.ToString() },
                Vectors = chunk.Embedding!,
                Payload =
                {
                    ["documentId"] = chunk.DocumentId.ToString(),
                    ["content"] = chunk.Content,
                    ["chunkIndex"] = chunk.ChunkIndex,
                    ["startPosition"] = chunk.StartPosition,
                    ["endPosition"] = chunk.EndPosition,
                    ["fileName"] = chunk.Metadata.GetValueOrDefault("fileName", ""),
                    ["contentType"] = chunk.Metadata.GetValueOrDefault("contentType", ""),
                    // Argha - 2026-02-19 - Store tags as list payload for keyword filtering
                    ["tags"] = tagsValue
                }
            };
        }).ToList();

        try
        {
            await ExecuteWithReinitAsync(
                ct => _client.UpsertAsync(_config.CollectionName, points, ct),
                cancellationToken);

            _logger.LogDebug("Upserted {Count} chunks to Qdrant", chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert chunks to Qdrant");
            throw;
        }
    }

    // Argha - 2026-02-19 - Added filterByTags; uses Must conditions per tag for AND semantics
    public async Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = BuildFilter(filterByDocumentId, filterByTags);

            var results = await ExecuteWithReinitAsync(
                ct => _client.SearchAsync(_config.CollectionName, queryEmbedding, (ulong)topK, filter, payloadSelector: true, ct: ct),
                cancellationToken);

            return results.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                Score = r.Score,
                ChunkIndex = (int)r.Payload["chunkIndex"].IntegerValue,
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue
                }
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Qdrant");
            throw;
        }
    }

    // Argha - 2026-02-20 - Same as SearchAsync but requests vectors back for MMR re-ranking
    public async Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = BuildFilter(filterByDocumentId, filterByTags);

            // Argha - 2026-02-20 - Pass vectorsSelector: true so ScoredPoint.Vectors is populated
            var results = await ExecuteWithReinitAsync(
                ct => _client.SearchAsync(_config.CollectionName, queryEmbedding, (ulong)topK, filter, payloadSelector: true, vectorsSelector: true, ct: ct),
                cancellationToken);

            return results.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                Score = r.Score,
                ChunkIndex = (int)r.Payload["chunkIndex"].IntegerValue,
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue
                },
                // Argha - 2026-02-20 - Convert protobuf RepeatedField<float> to float[] for MMR
                Embedding = r.Vectors?.Vector?.Data.ToArray()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Qdrant with embeddings");
            throw;
        }
    }

    // Argha - 2026-02-20 - Full-text keyword search using Qdrant payload index
    public async Task<List<SearchResult>> KeywordSearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Argha - 2026-02-20 - Add full-text match condition on 'content' field
            var conditions = new List<Condition>
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "content",
                        Match = new Match { Text = query }
                    }
                }
            };

            if (filterByDocumentId.HasValue)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "documentId",
                        Match = new Match { Keyword = filterByDocumentId.Value.ToString() }
                    }
                });
            }

            if (filterByTags?.Count > 0)
            {
                foreach (var tag in filterByTags)
                {
                    conditions.Add(new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "tags",
                            Match = new Match { Keyword = tag }
                        }
                    });
                }
            }

            var filter = new Filter();
            foreach (var c in conditions)
                filter.Must.Add(c);

            // Argha - 2026-02-20 - ScrollAsync returns ScrollResponse protobuf; access results via .Result
            var scrollResponse = await ExecuteWithReinitAsync(
                ct => _client.ScrollAsync(_config.CollectionName, filter, (uint)topK, payloadSelector: true, ct: ct),
                cancellationToken);

            return scrollResponse.Result.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                // Argha - 2026-02-20 - Score=1.0 placeholder; actual ranking done via RRF in RagService
                Score = 1.0,
                ChunkIndex = (int)r.Payload["chunkIndex"].IntegerValue,
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue
                }
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform keyword search in Qdrant");
            throw;
        }
    }

    public async Task DeleteDocumentChunksAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "documentId",
                            Match = new Match { Keyword = documentId.ToString() }
                        }
                    }
                }
            };

            await ExecuteWithReinitAsync(
                ct => _client.DeleteAsync(_config.CollectionName, filter, ct),
                cancellationToken);

            _logger.LogDebug("Deleted chunks for document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document chunks from Qdrant");
            throw;
        }
    }

    public async Task<VectorStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await ExecuteWithReinitAsync(
                ct => _client.GetCollectionInfoAsync(_config.CollectionName, ct),
                cancellationToken);

            return new VectorStoreStats
            {
                CollectionName = _config.CollectionName,
                TotalVectors = (long)info.PointsCount,
                VectorDimension = _embeddingService.EmbeddingDimension
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Qdrant stats");
            throw;
        }
    }

    // Argha - 2026-03-02 - #5 - Shared filter builder extracted to remove duplication across SearchAsync/SearchWithEmbeddingsAsync
    private static Filter? BuildFilter(Guid? filterByDocumentId, List<string>? filterByTags)
    {
        var conditions = new List<Condition>();

        if (filterByDocumentId.HasValue)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "documentId",
                    Match = new Match { Keyword = filterByDocumentId.Value.ToString() }
                }
            });
        }

        if (filterByTags?.Count > 0)
        {
            // Argha - 2026-02-19 - Each tag becomes a Must condition — AND semantics
            foreach (var tag in filterByTags)
            {
                conditions.Add(new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "tags",
                        Match = new Match { Keyword = tag }
                    }
                });
            }
        }

        if (conditions.Count == 0) return null;

        var filter = new Filter();
        foreach (var c in conditions)
            filter.Must.Add(c);
        return filter;
    }
}

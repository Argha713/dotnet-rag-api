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

    // Argha - 2026-03-04 - #17 - Public; replaces private EnsureCollectionAsync + InitializeAsync
    // Called at startup per-workspace and when a new workspace is created
    public async Task EnsureCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);

            if (!collections.Any(c => c == collectionName))
            {
                _logger.LogInformation("Creating Qdrant collection: {CollectionName}", collectionName);

                await _client.CreateCollectionAsync(
                    name: collectionName,
                    vectorParams: new VectorParams
                    {
                        Size = (ulong)_embeddingService.EmbeddingDimension,
                        Distance = Distance.Cosine
                    },
                    ct: cancellationToken);

                _logger.LogInformation("Collection created successfully: {CollectionName}", collectionName);
            }
            else
            {
                _logger.LogDebug("Collection {CollectionName} already exists", collectionName);
            }

            // Argha - 2026-02-20 - Create full-text payload index on 'content' for keyword search
            // Argha - 2026-03-06 - #18 - Add keyword index on 'documentId'; Qdrant now requires an index for filter-based deletes
            // CreatePayloadIndexAsync is idempotent — safe to call on every startup; backfills existing collections automatically
            await _client.CreatePayloadIndexAsync(
                name: collectionName,
                field: "content",
                schema: PayloadSchemaType.Text,
                ct: cancellationToken);
            _logger.LogDebug("Full-text payload index on 'content' ensured for {CollectionName}", collectionName);

            await _client.CreatePayloadIndexAsync(
                name: collectionName,
                field: "documentId",
                schema: PayloadSchemaType.Keyword,
                ct: cancellationToken);
            _logger.LogDebug("Keyword payload index on 'documentId' ensured for {CollectionName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-03-04 - #17 - Delete a workspace's Qdrant collection; called during workspace deletion cascade
    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteCollectionAsync(collectionName, cancellationToken);
            _logger.LogInformation("Deleted Qdrant collection: {CollectionName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-03-02 - #5 - Catches NotFound, reinitializes collection, retries once; covers all public methods
    // Argha - 2026-03-04 - #17 - Now accepts collectionName to pass through to EnsureCollectionAsync
    private async Task<T> ExecuteWithReinitAsync<T>(string collectionName, Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Qdrant collection '{Collection}' not found — reinitializing and retrying", collectionName);
            await _initLock.WaitAsync(ct);
            try
            {
                await EnsureCollectionAsync(collectionName, ct);
            }
            finally
            {
                _initLock.Release();
            }
            return await operation(ct);
        }
    }

    // Argha - 2026-03-02 - #5 - Void overload of ExecuteWithReinitAsync for methods that return Task
    // Argha - 2026-03-04 - #17 - Now accepts collectionName to pass through to EnsureCollectionAsync
    private async Task ExecuteWithReinitAsync(string collectionName, Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Qdrant collection '{Collection}' not found — reinitializing and retrying", collectionName);
            await _initLock.WaitAsync(ct);
            try
            {
                await EnsureCollectionAsync(collectionName, ct);
            }
            finally
            {
                _initLock.Release();
            }
            await operation(ct);
        }
    }

    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName for workspace isolation
    public async Task UpsertChunksAsync(string collectionName, List<DocumentChunk> chunks, CancellationToken cancellationToken = default)
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
                    // Argha - 2026-03-18 - #55 - Persist image metadata so search results can surface IsImage/ImageId
                    ["isImage"] = chunk.Metadata.GetValueOrDefault("isImage", "false"),
                    ["imageId"] = chunk.Metadata.GetValueOrDefault("imageId", ""),
                    // Argha - 2026-02-19 - Store tags as list payload for keyword filtering
                    ["tags"] = tagsValue
                }
            };
        }).ToList();

        try
        {
            await ExecuteWithReinitAsync(
                collectionName,
                ct => _client.UpsertAsync(collectionName, points, ct),
                cancellationToken);

            _logger.LogDebug("Upserted {Count} chunks to Qdrant collection {CollectionName}", chunks.Count, collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert chunks to Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-02-19 - Added filterByTags; uses Must conditions per tag for AND semantics
    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName for workspace isolation
    public async Task<List<SearchResult>> SearchAsync(
        string collectionName,
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
                collectionName,
                ct => _client.SearchAsync(collectionName, queryEmbedding, (ulong)topK, filter, payloadSelector: true, ct: ct),
                cancellationToken);

            return results.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                Score = r.Score,
                ChunkIndex = (int)r.Payload["chunkIndex"].IntegerValue,
                // Argha - 2026-03-18 - #55 - Read image metadata back from payload; guard against old points lacking the keys
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue,
                    ["isImage"] = r.Payload.TryGetValue("isImage", out var isImg1) ? isImg1.StringValue : "false",
                    ["imageId"] = r.Payload.TryGetValue("imageId", out var imgId1) ? imgId1.StringValue : ""
                }
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-02-20 - Same as SearchAsync but requests vectors back for MMR re-ranking
    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName
    public async Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        string collectionName,
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
                collectionName,
                ct => _client.SearchAsync(collectionName, queryEmbedding, (ulong)topK, filter, payloadSelector: true, vectorsSelector: true, ct: ct),
                cancellationToken);

            return results.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                Score = r.Score,
                ChunkIndex = (int)r.Payload["chunkIndex"].IntegerValue,
                // Argha - 2026-03-18 - #55 - Read image metadata back from payload; guard against old points lacking the keys
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue,
                    ["isImage"] = r.Payload.TryGetValue("isImage", out var isImg2) ? isImg2.StringValue : "false",
                    ["imageId"] = r.Payload.TryGetValue("imageId", out var imgId2) ? imgId2.StringValue : ""
                },
                // Argha - 2026-02-20 - Convert protobuf RepeatedField<float> to float[] for MMR
                Embedding = r.Vectors?.Vector?.Data.ToArray()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Qdrant with embeddings in collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-02-20 - Full-text keyword search using Qdrant payload index
    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName
    public async Task<List<SearchResult>> KeywordSearchAsync(
        string collectionName,
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
                collectionName,
                ct => _client.ScrollAsync(collectionName, filter, (uint)topK, payloadSelector: true, ct: ct),
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
                // Argha - 2026-03-18 - #55 - Read image metadata back from payload; guard against old points lacking the keys
                Metadata = new Dictionary<string, string>
                {
                    ["contentType"] = r.Payload["contentType"].StringValue,
                    ["isImage"] = r.Payload.TryGetValue("isImage", out var isImg3) ? isImg3.StringValue : "false",
                    ["imageId"] = r.Payload.TryGetValue("imageId", out var imgId3) ? imgId3.StringValue : ""
                }
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform keyword search in Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName
    public async Task DeleteDocumentChunksAsync(string collectionName, Guid documentId, CancellationToken cancellationToken = default)
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
                collectionName,
                ct => _client.DeleteAsync(collectionName, filter, ct),
                cancellationToken);

            _logger.LogDebug("Deleted chunks for document {DocumentId} from {CollectionName}", documentId, collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document chunks from Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    // Argha - 2026-03-04 - #17 - collectionName replaces _config.CollectionName
    public async Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await ExecuteWithReinitAsync(
                collectionName,
                ct => _client.GetCollectionInfoAsync(collectionName, ct),
                cancellationToken);

            return new VectorStoreStats
            {
                CollectionName = collectionName,
                TotalVectors = (long)info.PointsCount,
                VectorDimension = _embeddingService.EmbeddingDimension
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Qdrant stats for collection {CollectionName}", collectionName);
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

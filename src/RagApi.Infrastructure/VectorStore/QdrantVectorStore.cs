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
    private readonly QdrantClient _client;
    private readonly QdrantConfiguration _config;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        IOptions<QdrantConfiguration> config,
        IEmbeddingService embeddingService,
        ILogger<QdrantVectorStore> logger)
    {
        _config = config.Value;
        _embeddingService = embeddingService;
        _logger = logger;
        
        _client = new QdrantClient(
            host: _config.Host,
            port: _config.Port,
            https: _config.UseTls,
            apiKey: _config.ApiKey);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            
            if (!collections.Any(c => c == _config.CollectionName))
            {
                _logger.LogInformation("Creating Qdrant collection: {CollectionName}", _config.CollectionName);

                await _client.CreateCollectionAsync(
                    collectionName: _config.CollectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = (ulong)_embeddingService.EmbeddingDimension,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Collection created successfully");
            }
            else
            {
                _logger.LogDebug("Collection {CollectionName} already exists", _config.CollectionName);
            }

            // Argha - 2026-02-20 - Create full-text payload index on 'content' for keyword search (Phase 3.1)
            // CreatePayloadIndexAsync is idempotent — safe to call on every startup
            await _client.CreatePayloadIndexAsync(
                collectionName: _config.CollectionName,
                fieldName: "content",
                schemaType: PayloadSchemaType.Text,
                cancellationToken: cancellationToken);
            _logger.LogDebug("Full-text payload index on 'content' ensured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Qdrant collection");
            throw;
        }
    }

    public async Task UpsertChunksAsync(List<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0) return;

        var points = chunks.Select(chunk =>
        {
            // Argha - 2026-02-19 - Build tags ListValue for Qdrant payload (Phase 2.3)
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
                    // Argha - 2026-02-19 - Store tags as list payload for keyword filtering (Phase 2.3)
                    ["tags"] = tagsValue
                }
            };
        }).ToList();

        try
        {
            await _client.UpsertAsync(
                collectionName: _config.CollectionName,
                points: points,
                cancellationToken: cancellationToken);
            
            _logger.LogDebug("Upserted {Count} chunks to Qdrant", chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert chunks to Qdrant");
            throw;
        }
    }

    // Argha - 2026-02-19 - Added filterByTags; uses Must conditions per tag for AND semantics (Phase 2.3)
    public async Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Filter? filter = null;
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
                // Argha - 2026-02-19 - Each tag becomes a Must condition — AND semantics (Phase 2.3)
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

            if (conditions.Count > 0)
            {
                filter = new Filter();
                foreach (var c in conditions)
                    filter.Must.Add(c);
            }

            var results = await _client.SearchAsync(
                collectionName: _config.CollectionName,
                vector: queryEmbedding,
                limit: (ulong)topK,
                filter: filter,
                payloadSelector: true,
                cancellationToken: cancellationToken);

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

    // Argha - 2026-02-20 - Full-text keyword search using Qdrant payload index (Phase 3.1)
    public async Task<List<SearchResult>> KeywordSearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build filter conditions (same logic as SearchAsync)
            Filter? filter = null;
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

            // Argha - 2026-02-20 - Add full-text match condition on 'content' field (Phase 3.1)
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "content",
                    Match = new Match { Text = query }
                }
            });

            filter = new Filter();
            foreach (var c in conditions)
                filter.Must.Add(c);

            // Argha - 2026-02-20 - ScrollAsync returns ScrollResponse (protobuf); access .Result for the points list (Phase 3.1)
            var scrollResponse = await _client.ScrollAsync(
                collectionName: _config.CollectionName,
                filter: filter,
                limit: (uint)topK,
                payloadSelector: true,
                cancellationToken: cancellationToken);

            return scrollResponse.Result.Select(r => new SearchResult
            {
                ChunkId = Guid.Parse(r.Id.Uuid),
                DocumentId = Guid.Parse(r.Payload["documentId"].StringValue),
                FileName = r.Payload["fileName"].StringValue,
                Content = r.Payload["content"].StringValue,
                // Argha - 2026-02-20 - Score=1.0 placeholder; actual ranking done via RRF in RagService (Phase 3.1)
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

            await _client.DeleteAsync(
                collectionName: _config.CollectionName,
                filter: filter,
                cancellationToken: cancellationToken);
            
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
            var info = await _client.GetCollectionInfoAsync(_config.CollectionName, cancellationToken);
            
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
}

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

        var points = chunks.Select(chunk => new PointStruct
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
                ["contentType"] = chunk.Metadata.GetValueOrDefault("contentType", "")
            }
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

    public async Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Filter? filter = null;
            if (filterByDocumentId.HasValue)
            {
                filter = new Filter
                {
                    Must =
                    {
                        new Condition
                        {
                            Field = new FieldCondition
                            {
                                Key = "documentId",
                                Match = new Match { Keyword = filterByDocumentId.Value.ToString() }
                            }
                        }
                    }
                };
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

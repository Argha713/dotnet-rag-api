// Argha - 2026-02-21 - Azure AI Search IVectorStore implementation (Phase 5.1)
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.VectorStore;

/// <summary>
/// IVectorStore implementation backed by Azure AI Search.
/// Selected when VectorStore:Provider = "AzureAiSearch" in configuration.
/// Mirrors the payload schema used by QdrantVectorStore for consistency.
/// </summary>
public class AzureAiSearchVectorStore : IVectorStore
{
    // Argha - 2026-02-21 - Field names mirror the Qdrant payload schema for consistency
    private const string FieldId = "id";               // chunk GUID (index key)
    private const string FieldDocumentId = "documentId";
    private const string FieldContent = "content";
    private const string FieldFileName = "fileName";
    private const string FieldChunkIndex = "chunkIndex";
    private const string FieldStartPosition = "startPosition";
    private const string FieldEndPosition = "endPosition";
    private const string FieldContentType = "contentType";
    private const string FieldTags = "tags";
    private const string FieldEmbedding = "embedding";

    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly AzureAiSearchSettings _settings;
    private readonly ILogger<AzureAiSearchVectorStore> _logger;

    public AzureAiSearchVectorStore(
        SearchIndexClient indexClient,
        SearchClient searchClient,
        IOptions<VectorStoreConfiguration> config,
        ILogger<AzureAiSearchVectorStore> logger)
    {
        _indexClient = indexClient;
        _searchClient = searchClient;
        _settings = config.Value.AzureAiSearch;
        _logger = logger;
    }

    // ── InitializeAsync ────────────────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Ensuring Azure AI Search index '{IndexName}' exists with dimension {Dim}",
            _settings.IndexName, _settings.EmbeddingDimension);

        var index = BuildIndexDefinition();
        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);

        _logger.LogInformation("Azure AI Search index ready: {IndexName}", _settings.IndexName);
    }

    // ── UpsertChunksAsync ──────────────────────────────────────────────────────

    public async Task UpsertChunksAsync(
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        var documents = chunks.Select(MapChunkToDocument).ToList();
        var batch = IndexDocumentsBatch.MergeOrUpload(documents);
        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        _logger.LogDebug("Upserted {Count} chunks to Azure AI Search", chunks.Count);
    }

    // ── SearchAsync ────────────────────────────────────────────────────────────

    public async Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        var options = BuildVectorSearchOptions(
            topK,
            filterByDocumentId,
            filterByTags,
            includeEmbedding: false,
            queryEmbedding);

        // Argha - 2026-02-21 - Empty string triggers vector-only search (no BM25 scoring)
        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: string.Empty,
            options,
            cancellationToken);

        return await MapResultsAsync(response, includeEmbedding: false, cancellationToken);
    }

    // ── SearchWithEmbeddingsAsync ──────────────────────────────────────────────

    public async Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        var options = BuildVectorSearchOptions(
            topK,
            filterByDocumentId,
            filterByTags,
            includeEmbedding: true,
            queryEmbedding);

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: string.Empty,
            options,
            cancellationToken);

        return await MapResultsAsync(response, includeEmbedding: true, cancellationToken);
    }

    // ── KeywordSearchAsync ─────────────────────────────────────────────────────

    public async Task<List<SearchResult>> KeywordSearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        // Argha - 2026-02-21 - BM25 text query; Score = 1.0 per IVectorStore interface contract (RRF caller re-ranks)
        var options = new SearchOptions
        {
            Size = topK,
            Filter = BuildODataFilter(filterByDocumentId, filterByTags)
        };
        options.Select.Add(FieldId);
        options.Select.Add(FieldDocumentId);
        options.Select.Add(FieldContent);
        options.Select.Add(FieldFileName);
        options.Select.Add(FieldChunkIndex);
        options.Select.Add(FieldContentType);

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: query,
            options,
            cancellationToken);

        var results = new List<SearchResult>();
        await foreach (var page in response.Value.GetResultsAsync().AsPages().WithCancellation(cancellationToken))
        {
            foreach (var item in page.Values)
                results.Add(MapDocumentToSearchResult(item.Document, score: 1.0, embedding: null));
        }

        return results;
    }

    // ── DeleteDocumentChunksAsync ──────────────────────────────────────────────

    public async Task DeleteDocumentChunksAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        // Argha - 2026-02-21 - Fetch all chunk IDs for the document, then delete in one batch
        var options = new SearchOptions
        {
            Filter = $"{FieldDocumentId} eq '{documentId}'",
            Size = 1000
        };
        options.Select.Add(FieldId);

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: string.Empty,
            options,
            cancellationToken);

        var ids = new List<string>();
        await foreach (var page in response.Value.GetResultsAsync().AsPages().WithCancellation(cancellationToken))
        {
            foreach (var item in page.Values)
            {
                if (item.Document.TryGetValue(FieldId, out var idVal) && idVal is string id)
                    ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            _logger.LogDebug("No chunks found for documentId {DocumentId} — nothing to delete", documentId);
            return;
        }

        var deleteDocuments = ids.Select(id =>
        {
            var doc = new SearchDocument();
            doc[FieldId] = id;
            return doc;
        }).ToList();

        var batch = IndexDocumentsBatch.Delete(deleteDocuments);
        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        _logger.LogDebug("Deleted {Count} chunks for documentId {DocumentId}", ids.Count, documentId);
    }

    // ── GetStatsAsync ──────────────────────────────────────────────────────────

    public async Task<VectorStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _indexClient.GetIndexStatisticsAsync(
            _settings.IndexName,
            cancellationToken);

        return new VectorStoreStats
        {
            TotalVectors = stats.Value.DocumentCount,
            TotalDocuments = stats.Value.DocumentCount,
            CollectionName = _settings.IndexName,
            VectorDimension = _settings.EmbeddingDimension
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private SearchIndex BuildIndexDefinition()
    {
        var vectorProfile = new VectorSearchProfile("hnsw-profile", "hnsw-config");
        var hnswAlgorithm = new HnswAlgorithmConfiguration("hnsw-config");
        var vectorSearch = new VectorSearch();
        vectorSearch.Profiles.Add(vectorProfile);
        vectorSearch.Algorithms.Add(hnswAlgorithm);

        // Argha - 2026-02-21 - SimpleField has no IsRetrievable; fields are returned by default (use IsHidden=true to hide)
        var index = new SearchIndex(_settings.IndexName)
        {
            VectorSearch = vectorSearch,
            Fields =
            {
                new SimpleField(FieldId, SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SimpleField(FieldDocumentId, SearchFieldDataType.String) { IsFilterable = true },
                new SearchableField(FieldContent),
                new SimpleField(FieldFileName, SearchFieldDataType.String),
                new SimpleField(FieldChunkIndex, SearchFieldDataType.Int32),
                new SimpleField(FieldStartPosition, SearchFieldDataType.Int32),
                new SimpleField(FieldEndPosition, SearchFieldDataType.Int32),
                new SimpleField(FieldContentType, SearchFieldDataType.String),
                new SimpleField(FieldTags, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                new VectorSearchField(FieldEmbedding, _settings.EmbeddingDimension, "hnsw-profile")
            }
        };

        return index;
    }

    private SearchOptions BuildVectorSearchOptions(
        int topK,
        Guid? filterByDocumentId,
        List<string>? filterByTags,
        bool includeEmbedding,
        float[] queryEmbedding)
    {
        var vectorQuery = new VectorizedQuery(queryEmbedding)
        {
            KNearestNeighborsCount = topK,
            Fields = { FieldEmbedding }
        };

        var options = new SearchOptions
        {
            Size = topK,
            Filter = BuildODataFilter(filterByDocumentId, filterByTags),
            VectorSearch = new VectorSearchOptions { Queries = { vectorQuery } }
        };

        options.Select.Add(FieldId);
        options.Select.Add(FieldDocumentId);
        options.Select.Add(FieldContent);
        options.Select.Add(FieldFileName);
        options.Select.Add(FieldChunkIndex);
        options.Select.Add(FieldContentType);

        if (includeEmbedding)
            options.Select.Add(FieldEmbedding);

        return options;
    }

    private static string? BuildODataFilter(Guid? filterByDocumentId, List<string>? filterByTags)
    {
        var parts = new List<string>();

        if (filterByDocumentId.HasValue)
            parts.Add($"{FieldDocumentId} eq '{filterByDocumentId.Value}'");

        if (filterByTags is { Count: > 0 })
        {
            // Argha - 2026-02-21 - OData any() matches a document if it contains ANY of the specified tags (OR semantics)
            // This differs from Qdrant which uses AND semantics; kept as OR here per Azure AI Search best practice
            var tagFilters = filterByTags
                .Select(t => $"{FieldTags}/any(tag: tag eq '{t.Replace("'", "''")}')")
                .ToList();
            parts.Add("(" + string.Join(" or ", tagFilters) + ")");
        }

        return parts.Count > 0 ? string.Join(" and ", parts) : null;
    }

    private static async Task<List<SearchResult>> MapResultsAsync(
        Response<SearchResults<SearchDocument>> response,
        bool includeEmbedding,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        await foreach (var page in response.Value.GetResultsAsync().AsPages().WithCancellation(cancellationToken))
        {
            foreach (var item in page.Values)
            {
                float[]? embedding = null;
                if (includeEmbedding
                    && item.Document.TryGetValue(FieldEmbedding, out var embVal)
                    && embVal is IEnumerable<object> embArr)
                {
                    embedding = embArr.Select(v => Convert.ToSingle(v)).ToArray();
                }

                results.Add(MapDocumentToSearchResult(
                    item.Document,
                    score: item.Score ?? 0.0,
                    embedding));
            }
        }

        return results;
    }

    private static SearchResult MapDocumentToSearchResult(
        SearchDocument doc,
        double score,
        float[]? embedding)
    {
        Guid.TryParse(doc.GetString(FieldId), out var chunkId);
        Guid.TryParse(doc.GetString(FieldDocumentId), out var documentId);

        return new SearchResult
        {
            ChunkId = chunkId,
            DocumentId = documentId,
            Content = doc.GetString(FieldContent) ?? string.Empty,
            FileName = doc.GetString(FieldFileName) ?? string.Empty,
            ChunkIndex = doc.TryGetValue(FieldChunkIndex, out var ci) ? Convert.ToInt32(ci) : 0,
            Score = score,
            Metadata = new Dictionary<string, string>
            {
                ["contentType"] = doc.GetString(FieldContentType) ?? string.Empty
            },
            Embedding = embedding
        };
    }

    private static SearchDocument MapChunkToDocument(DocumentChunk chunk)
    {
        return new SearchDocument
        {
            [FieldId] = chunk.Id.ToString(),
            [FieldDocumentId] = chunk.DocumentId.ToString(),
            [FieldContent] = chunk.Content,
            [FieldFileName] = chunk.Metadata.GetValueOrDefault("fileName", ""),
            [FieldChunkIndex] = chunk.ChunkIndex,
            [FieldStartPosition] = chunk.StartPosition,
            [FieldEndPosition] = chunk.EndPosition,
            [FieldContentType] = chunk.Metadata.GetValueOrDefault("contentType", ""),
            [FieldTags] = chunk.Tags,
            [FieldEmbedding] = chunk.Embedding
        };
    }
}

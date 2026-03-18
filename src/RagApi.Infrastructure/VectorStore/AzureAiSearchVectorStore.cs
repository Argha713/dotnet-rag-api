// Argha - 2026-02-21 - Azure AI Search IVectorStore implementation
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
    // Argha - 2026-03-18 - #55 - Image metadata fields mirroring Qdrant payload schema
    private const string FieldIsImage = "isImage";
    private const string FieldImageId = "imageId";

    private readonly SearchIndexClient _indexClient;
    private readonly AzureAiSearchSettings _settings;
    private readonly ILogger<AzureAiSearchVectorStore> _logger;

    public AzureAiSearchVectorStore(
        SearchIndexClient indexClient,
        IOptions<VectorStoreConfiguration> config,
        ILogger<AzureAiSearchVectorStore> logger)
    {
        _indexClient = indexClient;
        _settings = config.Value.AzureAiSearch;
        _logger = logger;
    }

    // ── EnsureCollectionAsync ──────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName is used as the Azure Search index name
    public async Task EnsureCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Ensuring Azure AI Search index '{IndexName}' exists with dimension {Dim}",
            collectionName, _settings.EmbeddingDimension);

        var index = BuildIndexDefinition(collectionName);
        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);

        _logger.LogInformation("Azure AI Search index ready: {IndexName}", collectionName);
    }

    // ── DeleteCollectionAsync ──────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - Delete the Azure AI Search index for workspace deletion cascade
    public async Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexClient.DeleteIndexAsync(collectionName, cancellationToken);
            _logger.LogInformation("Deleted Azure AI Search index: {IndexName}", collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Azure AI Search index {IndexName}", collectionName);
            throw;
        }
    }

    // ── UpsertChunksAsync ──────────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task UpsertChunksAsync(
        string collectionName,
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return;

        var searchClient = _indexClient.GetSearchClient(collectionName);
        var documents = chunks.Select(MapChunkToDocument).ToList();
        var batch = IndexDocumentsBatch.MergeOrUpload(documents);
        await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        _logger.LogDebug("Upserted {Count} chunks to Azure AI Search index {IndexName}", chunks.Count, collectionName);
    }

    // ── SearchAsync ────────────────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task<List<SearchResult>> SearchAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        var searchClient = _indexClient.GetSearchClient(collectionName);
        var options = BuildVectorSearchOptions(
            topK,
            filterByDocumentId,
            filterByTags,
            includeEmbedding: false,
            queryEmbedding);

        // Argha - 2026-02-21 - Empty string triggers vector-only search (no BM25 scoring)
        var response = await searchClient.SearchAsync<SearchDocument>(
            searchText: string.Empty,
            options,
            cancellationToken);

        return await MapResultsAsync(response, includeEmbedding: false, cancellationToken);
    }

    // ── SearchWithEmbeddingsAsync ──────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        var searchClient = _indexClient.GetSearchClient(collectionName);
        var options = BuildVectorSearchOptions(
            topK,
            filterByDocumentId,
            filterByTags,
            includeEmbedding: true,
            queryEmbedding);

        var response = await searchClient.SearchAsync<SearchDocument>(
            searchText: string.Empty,
            options,
            cancellationToken);

        return await MapResultsAsync(response, includeEmbedding: true, cancellationToken);
    }

    // ── KeywordSearchAsync ─────────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task<List<SearchResult>> KeywordSearchAsync(
        string collectionName,
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default)
    {
        var searchClient = _indexClient.GetSearchClient(collectionName);
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
        // Argha - 2026-03-18 - #55 - Select image metadata fields so they are returned in results
        options.Select.Add(FieldIsImage);
        options.Select.Add(FieldImageId);

        var response = await searchClient.SearchAsync<SearchDocument>(
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

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task DeleteDocumentChunksAsync(
        string collectionName,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var searchClient = _indexClient.GetSearchClient(collectionName);
        // Argha - 2026-02-21 - Fetch all chunk IDs for the document, then delete in one batch
        var options = new SearchOptions
        {
            Filter = $"{FieldDocumentId} eq '{documentId}'",
            Size = 1000
        };
        options.Select.Add(FieldId);

        var response = await searchClient.SearchAsync<SearchDocument>(
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
        await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

        _logger.LogDebug("Deleted {Count} chunks for documentId {DocumentId} from index {IndexName}", ids.Count, documentId, collectionName);
    }

    // ── GetStatsAsync ──────────────────────────────────────────────────────────

    // Argha - 2026-03-04 - #17 - collectionName used as index name
    public async Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var stats = await _indexClient.GetIndexStatisticsAsync(
            collectionName,
            cancellationToken);

        return new VectorStoreStats
        {
            TotalVectors = stats.Value.DocumentCount,
            TotalDocuments = stats.Value.DocumentCount,
            CollectionName = collectionName,
            VectorDimension = _settings.EmbeddingDimension
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private SearchIndex BuildIndexDefinition(string indexName)
    {
        var vectorProfile = new VectorSearchProfile("hnsw-profile", "hnsw-config");
        var hnswAlgorithm = new HnswAlgorithmConfiguration("hnsw-config");
        var vectorSearch = new VectorSearch();
        vectorSearch.Profiles.Add(vectorProfile);
        vectorSearch.Algorithms.Add(hnswAlgorithm);

        // Argha - 2026-02-21 - SimpleField has no IsRetrievable; fields are returned by default (use IsHidden=true to hide)
        var index = new SearchIndex(indexName)
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
                // Argha - 2026-03-18 - #55 - Image metadata fields
                new SimpleField(FieldIsImage, SearchFieldDataType.String),
                new SimpleField(FieldImageId, SearchFieldDataType.String),
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
        // Argha - 2026-03-18 - #55 - Select image metadata fields so they are returned in results
        options.Select.Add(FieldIsImage);
        options.Select.Add(FieldImageId);

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
            // Argha - 2026-03-18 - #55 - Read image metadata back; guard against old index docs lacking the keys
            Metadata = new Dictionary<string, string>
            {
                ["contentType"] = doc.GetString(FieldContentType) ?? string.Empty,
                ["isImage"] = doc.GetString(FieldIsImage) ?? "false",
                ["imageId"] = doc.GetString(FieldImageId) ?? ""
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
            // Argha - 2026-03-18 - #55 - Persist image metadata so search results can surface IsImage/ImageId
            [FieldIsImage] = chunk.Metadata.GetValueOrDefault("isImage", "false"),
            [FieldImageId] = chunk.Metadata.GetValueOrDefault("imageId", ""),
            [FieldTags] = chunk.Tags,
            [FieldEmbedding] = chunk.Embedding
        };
    }
}

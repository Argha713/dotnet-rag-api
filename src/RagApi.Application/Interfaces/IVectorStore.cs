using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

/// <summary>
/// Interface for vector database operations.
/// All methods accept collectionName as the first parameter so callers
/// (Scoped services) can pass the workspace's collection without violating
/// the Singleton lifetime of the implementation.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Idempotent: create the named collection if it does not exist, then ensure indexes.
    /// Called at startup for each workspace, and on-demand when a new workspace is created.
    /// </summary>
    // Argha - 2026-03-04 - #17 - Replaces InitializeAsync(); collectionName is workspace-specific
    Task EnsureCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the named collection entirely. Used when a workspace is deleted.
    /// </summary>
    // Argha - 2026-03-04 - #17 - New method for workspace deletion cascade
    Task DeleteCollectionAsync(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store document chunks with their embeddings in the specified collection.
    /// </summary>
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task UpsertChunksAsync(string collectionName, List<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for similar chunks using a query embedding.
    /// </summary>
    // Argha - 2026-02-19 - Added filterByTags parameter for metadata tag filtering
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task<List<SearchResult>> SearchAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for similar chunks and include their vector embeddings in results (used by MMR re-ranking).
    /// </summary>
    // Argha - 2026-02-20 - Returns results with Embedding populated for MMR re-ranking
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        string collectionName,
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for chunks matching a keyword query using full-text index.
    /// </summary>
    // Argha - 2026-02-20 - Keyword search for hybrid search feature
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task<List<SearchResult>> KeywordSearchAsync(
        string collectionName,
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all chunks for a specific document from the specified collection.
    /// </summary>
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task DeleteDocumentChunksAsync(string collectionName, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about the specified collection.
    /// </summary>
    // Argha - 2026-03-04 - #17 - Added collectionName param for workspace isolation
    Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken = default);
}

public class VectorStoreStats
{
    public long TotalVectors { get; set; }
    public long TotalDocuments { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public int VectorDimension { get; set; }
}

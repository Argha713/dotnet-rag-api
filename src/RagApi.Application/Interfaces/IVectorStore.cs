using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

/// <summary>
/// Interface for vector database operations
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Initialize the vector store (create collection if needed)
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Store document chunks with their embeddings
    /// </summary>
    Task UpsertChunksAsync(List<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Search for similar chunks using a query embedding
    /// </summary>
    // Argha - 2026-02-19 - Added filterByTags parameter for metadata tag filtering 
    Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Search for similar chunks and include their vector embeddings in the results.
    /// Used by MMR re-ranking which needs cross-similarities between result chunks.
    /// </summary>
    // Argha - 2026-02-20 - Returns results with Embedding populated for MMR re-ranking 
    Task<List<SearchResult>> SearchWithEmbeddingsAsync(
        float[] queryEmbedding,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for chunks matching a keyword query using full-text index.
    /// Returns results with Score = 1.0 (rank-based fusion is applied by the caller).
    /// </summary>
    // Argha - 2026-02-20 - Keyword search for hybrid search feature 
    Task<List<SearchResult>> KeywordSearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all chunks for a specific document
    /// </summary>
    Task DeleteDocumentChunksAsync(Guid documentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get statistics about the vector store
    /// </summary>
    Task<VectorStoreStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

public class VectorStoreStats
{
    public long TotalVectors { get; set; }
    public long TotalDocuments { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public int VectorDimension { get; set; }
}

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
    Task<List<SearchResult>> SearchAsync(
        float[] queryEmbedding, 
        int topK = 5,
        Guid? filterByDocumentId = null,
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

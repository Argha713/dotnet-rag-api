namespace RagApi.Application.Interfaces;

/// <summary>
/// Interface for generating text embeddings
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generate embedding vector for a single text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate embedding vectors for multiple texts (batch)
    /// </summary>
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the dimension of the embedding vectors
    /// </summary>
    int EmbeddingDimension { get; }
    
    /// <summary>
    /// Get the name of the model being used
    /// </summary>
    string ModelName { get; }
}

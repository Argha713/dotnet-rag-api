using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

/// <summary>
/// Interface for processing and chunking documents
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Extract text content from a document
    /// </summary>
    Task<string> ExtractTextAsync(Stream fileStream, string contentType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Split text into chunks for embedding
    /// </summary>
    List<DocumentChunk> ChunkText(Guid documentId, string text, ChunkingOptions? options = null);
    
    /// <summary>
    /// Check if the content type is supported
    /// </summary>
    bool IsSupported(string contentType);
    
    /// <summary>
    /// Get list of supported content types
    /// </summary>
    IReadOnlyList<string> SupportedContentTypes { get; }
}

public class ChunkingOptions
{
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public string SeparatorPattern { get; set; } = @"\n\n|\r\n\r\n";
}

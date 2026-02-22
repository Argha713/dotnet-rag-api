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

// Argha - 2026-02-20 - Chunking strategy selection
public enum ChunkingStrategy
{
    /// <summary>Character-count-based splitting with overlap at paragraph boundaries (default).</summary>
    Fixed,
    /// <summary>Splits at sentence boundaries (.!?) and groups sentences until ChunkSize is reached.</summary>
    Sentence,
    /// <summary>Each blank-line-separated paragraph becomes exactly one chunk (no size limit imposed).</summary>
    Paragraph
}

public class ChunkingOptions
{
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 200;
    public string SeparatorPattern { get; set; } = @"\n\n|\r\n\r\n";
    // Argha - 2026-02-20 - Strategy selection; defaults to Fixed (existing behaviour) 
    public ChunkingStrategy Strategy { get; set; } = ChunkingStrategy.Fixed;
}

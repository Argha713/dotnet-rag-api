namespace RagApi.Application.Models;

/// <summary>
/// Configuration options for document processing and chunking
/// </summary>
// Argha - 2026-02-20 - Config POCO for configurable chunking strategies (Phase 3.3)
public class DocumentProcessingOptions
{
    public const string SectionName = "DocumentProcessing";

    /// <summary>
    /// Default chunking strategy when none is specified per-upload.
    /// Valid values: "Fixed", "Sentence", "Paragraph".
    /// </summary>
    public string DefaultChunkingStrategy { get; set; } = "Fixed";

    /// <summary>
    /// Maximum characters per chunk (used by Fixed and Sentence strategies).
    /// </summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>
    /// Overlap characters carried from the end of one chunk into the start of the next (Fixed strategy only).
    /// </summary>
    public int ChunkOverlap { get; set; } = 200;
}

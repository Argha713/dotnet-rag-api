namespace RagApi.Domain.Entities;

/// <summary>
/// Represents a chunk of text from a document with its embedding
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public float[]? Embedding { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();

    // Argha - 2026-02-19 - Tags propagated from parent document for Qdrant payload filtering (Phase 2.3)
    public List<string> Tags { get; set; } = new();
}

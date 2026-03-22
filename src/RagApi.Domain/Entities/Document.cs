namespace RagApi.Domain.Entities;

/// <summary>
/// Represents an uploaded document in the RAG system
/// </summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }

    // Argha - 2026-02-19 - JSON array of tags for metadata filtering
    public string TagsJson { get; set; } = "[]";

    // Argha - 2026-02-21 - Timestamp of last re-process; null for documents never updated
    public DateTime? UpdatedAt { get; set; }

    // Argha - 2026-03-22 - #78 - Document version; incremented on each UpdateDocumentAsync call
    public int Version { get; set; } = 1;

    // Argha - 2026-03-22 - #78 - SHA-256 hash of file bytes for deduplication; null for legacy docs
    public string? ContentHash { get; set; }

    // Argha - 2026-03-22 - #79 - Lazily generated summary text; null until first GET /summary request
    public string? SummaryText { get; set; }

    // Argha - 2026-03-22 - #79 - Timestamp when SummaryText was generated; used to check staleness
    public DateTime? SummaryGeneratedAt { get; set; }

    // Argha - 2026-03-22 - #80 - JSON array of LLM-suggested tags; reset to "[]" on SetTagsAsync
    public string SuggestedTagsJson { get; set; } = "[]";

    // Argha - 2026-03-22 - #78 - Transient flag; set to true by UploadDocumentAsync when a duplicate is found.
    // Not persisted — excluded via Fluent API in RagApiDbContext.
    public bool IsDuplicate { get; set; }

    // Argha - 2026-03-04 - #17 - FK to Workspaces; all document operations are scoped by workspace
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    // Argha - 2026-03-16 - #30 - Navigation to extracted images; populated during Phase 14 ingestion
    public ICollection<DocumentImage> Images { get; set; } = new List<DocumentImage>();
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

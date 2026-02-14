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
}

public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

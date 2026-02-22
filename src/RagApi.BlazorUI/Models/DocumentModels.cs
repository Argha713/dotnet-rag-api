namespace RagApi.BlazorUI.Models;

// Argha - 2026-02-21 - Document DTOs mirroring RagApi.Api.Models.DocumentDto 

public class DocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}

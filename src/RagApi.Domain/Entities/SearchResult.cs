namespace RagApi.Domain.Entities;

/// <summary>
/// Represents a semantic search result
/// </summary>
public class SearchResult
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public int ChunkIndex { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

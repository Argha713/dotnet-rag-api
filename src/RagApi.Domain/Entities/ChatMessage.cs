namespace RagApi.Domain.Entities;

/// <summary>
/// Represents a chat message in a conversation
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a chat response with source citations
/// </summary>
public class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceCitation> Sources { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
}

/// <summary>
/// Represents a source citation from a document chunk
/// </summary>
public class SourceCitation
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelevantText { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
    public int ChunkIndex { get; set; }
}

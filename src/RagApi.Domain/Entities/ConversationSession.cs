namespace RagApi.Domain.Entities;

// Argha - 2026-02-19 - Server-side conversation session entity (Phase 2.2)

/// <summary>
/// Represents a server-side conversation session persisted in SQLite
/// </summary>
public class ConversationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Auto-populated from the first 80 characters of the first user message
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// JSON-serialized List&lt;ChatMessage&gt; — deserialized by ConversationRepository
    /// </summary>
    public string MessagesJson { get; set; } = "[]";
}

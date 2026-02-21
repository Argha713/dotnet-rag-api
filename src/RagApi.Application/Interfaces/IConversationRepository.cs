using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-02-19 - Repository interface for server-side conversation sessions 

/// <summary>
/// Data access interface for conversation sessions
/// </summary>
public interface IConversationRepository
{
    /// <summary>Creates a new empty session and persists it</summary>
    Task<ConversationSession> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the session, or null if not found</summary>
    Task<ConversationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a user message and an assistant message to the session atomically.
    /// Also sets Title from the first user message and updates LastMessageAt.
    /// Returns false if the session does not exist.
    /// </summary>
    Task<bool> AppendMessagesAsync(
        Guid id,
        string userQuery,
        string assistantAnswer,
        CancellationToken cancellationToken = default);

    /// <summary>Returns false if the session does not exist</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

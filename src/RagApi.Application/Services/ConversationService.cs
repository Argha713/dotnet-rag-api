using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

// Argha - 2026-02-19 - Conversation session orchestration service 

/// <summary>
/// Manages server-side conversation sessions and history retrieval for RagService
/// </summary>
public class ConversationService
{
    private readonly IConversationRepository _repository;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(IConversationRepository repository, ILogger<ConversationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Creates a new empty conversation session</summary>
    public async Task<ConversationSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating new conversation session");
        return await _repository.CreateAsync(cancellationToken);
    }

    /// <summary>Returns the session, or null if not found</summary>
    public async Task<ConversationSession?> GetSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _repository.GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Returns the deserialized message history ready for RagService, or null if the session does not exist.
    /// An existing session with no messages returns an empty list.
    /// </summary>
    public async Task<List<ChatMessage>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(id, cancellationToken);
        if (session == null) return null;

        return JsonSerializer.Deserialize<List<ChatMessage>>(session.MessagesJson)
               ?? new List<ChatMessage>();
    }

    /// <summary>
    /// Appends the user query and assistant answer to the session.
    /// Returns false if the session does not exist.
    /// </summary>
    public async Task<bool> AppendMessagesAsync(
        Guid id,
        string userQuery,
        string assistantAnswer,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Appending messages to session {SessionId}", id);
        return await _repository.AppendMessagesAsync(id, userQuery, assistantAnswer, cancellationToken);
    }

    /// <summary>Returns false if the session does not exist</summary>
    public async Task<bool> DeleteSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting conversation session {SessionId}", id);
        return await _repository.DeleteAsync(id, cancellationToken);
    }
}

using System.Text.Json;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-19 - SQLite-backed conversation session repository (Phase 2.2)
public class ConversationRepository : IConversationRepository
{
    private readonly RagApiDbContext _dbContext;

    // Argha - 2026-02-19 - PascalCase matches ChatMessage property names; must be consistent with deserialization
    private static readonly JsonSerializerOptions JsonOpts = new();

    public ConversationRepository(RagApiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConversationSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var session = new ConversationSession();
        _dbContext.ConversationSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ConversationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ConversationSessions.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<bool> AppendMessagesAsync(
        Guid id,
        string userQuery,
        string assistantAnswer,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ConversationSessions.FindAsync(new object[] { id }, cancellationToken);
        if (session == null) return false;

        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(session.MessagesJson, JsonOpts)
                       ?? new List<ChatMessage>();

        messages.Add(new ChatMessage { Role = "user", Content = userQuery });
        messages.Add(new ChatMessage { Role = "assistant", Content = assistantAnswer });

        // Argha - 2026-02-19 - Set title once from first user message, truncated to 80 chars
        if (session.Title == null)
        {
            var firstUserMessage = messages.First(m => m.Role == "user").Content;
            session.Title = firstUserMessage.Length > 80 ? firstUserMessage[..80] : firstUserMessage;
        }

        session.MessagesJson = JsonSerializer.Serialize(messages, JsonOpts);
        session.LastMessageAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ConversationSessions.FindAsync(new object[] { id }, cancellationToken);
        if (session == null) return false;

        _dbContext.ConversationSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

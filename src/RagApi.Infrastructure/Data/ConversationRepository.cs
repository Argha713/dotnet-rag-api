using System.Text.Json;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-19 - PostgreSQL-backed conversation session repository
// Argha - 2026-03-04 - #17 - All queries scoped to the current workspace via IWorkspaceContext
public class ConversationRepository : IConversationRepository
{
    private readonly RagApiDbContext _dbContext;
    // Argha - 2026-03-04 - #17 - Scoped workspace context; populated by ApiKeyMiddleware each request
    private readonly IWorkspaceContext _workspaceContext;

    // Argha - 2026-02-19 - PascalCase matches ChatMessage property names; must be consistent with deserialization
    private static readonly JsonSerializerOptions JsonOpts = new();

    public ConversationRepository(RagApiDbContext dbContext, IWorkspaceContext workspaceContext)
    {
        _dbContext = dbContext;
        _workspaceContext = workspaceContext;
    }

    public async Task<ConversationSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        var session = new ConversationSession
        {
            // Argha - 2026-03-04 - #17 - Bind session to current workspace at creation time
            WorkspaceId = _workspaceContext.Current.Id
        };
        _dbContext.ConversationSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ConversationSession?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-04 - #17 - WorkspaceId filter prevents cross-tenant session access
        return await _dbContext.ConversationSessions
            .FirstOrDefaultAsync(
                s => s.Id == id && s.WorkspaceId == _workspaceContext.Current.Id,
                cancellationToken);
    }

    public async Task<bool> AppendMessagesAsync(
        Guid id,
        string userQuery,
        string assistantAnswer,
        CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-04 - #17 - Workspace filter ensures we only update sessions we own
        var session = await _dbContext.ConversationSessions
            .FirstOrDefaultAsync(
                s => s.Id == id && s.WorkspaceId == _workspaceContext.Current.Id,
                cancellationToken);
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
        // Argha - 2026-03-04 - #17 - Workspace filter ensures we only delete sessions we own
        var session = await _dbContext.ConversationSessions
            .FirstOrDefaultAsync(
                s => s.Id == id && s.WorkspaceId == _workspaceContext.Current.Id,
                cancellationToken);
        if (session == null) return false;

        _dbContext.ConversationSessions.Remove(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Argha - 2026-03-15 - #24 - List all sessions for the current workspace, newest-first
    public async Task<List<ConversationSession>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ConversationSessions
            .Where(s => s.WorkspaceId == _workspaceContext.Current.Id)
            .OrderByDescending(s => s.LastMessageAt)
            .ToListAsync(cancellationToken);
    }
}

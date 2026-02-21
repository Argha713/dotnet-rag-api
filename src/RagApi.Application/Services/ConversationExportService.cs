using System.Text;
using System.Text.Json;
using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

// Argha - 2026-02-21 - Conversation export service

/// <summary>
/// Represents the result of a conversation export: pre-serialized bytes with metadata for the HTTP response.
/// </summary>
public record ConversationExportResult(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Formats a conversation session for download as JSON, Markdown, or plain text.
/// </summary>
public class ConversationExportService
{
    private readonly ConversationService _conversationService;

    // Argha - 2026-02-21 - Indented JSON for human-readable export
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public ConversationExportService(ConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    /// <summary>
    /// Exports the specified session in the requested format.
    /// Returns <c>null</c> if the session does not exist.
    /// Throws <see cref="ArgumentException"/> for unrecognised format strings.
    /// Supported formats: <c>json</c>, <c>markdown</c> (alias <c>md</c>), <c>text</c> (alias <c>txt</c>).
    /// </summary>
    public async Task<ConversationExportResult?> ExportAsync(
        Guid sessionId,
        string format,
        CancellationToken cancellationToken = default)
    {
        var session = await _conversationService.GetSessionAsync(sessionId, cancellationToken);
        if (session == null) return null;

        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(session.MessagesJson)
                       ?? new List<ChatMessage>();

        return format.ToLowerInvariant() switch
        {
            "json"              => FormatJson(session, messages),
            "markdown" or "md"  => FormatMarkdown(session, messages),
            "text" or "txt"     => FormatText(session, messages),
            _ => throw new ArgumentException(
                $"Unsupported export format '{format}'. Supported values: json, markdown, text.")
        };
    }

    // --- private formatters ---

    private static ConversationExportResult FormatJson(ConversationSession session, List<ChatMessage> messages)
    {
        // Argha - 2026-02-21 - Anonymous type gives full control over JSON field names and shape
        var export = new
        {
            sessionId     = session.Id,
            title         = session.Title,
            createdAt     = session.CreatedAt,
            lastMessageAt = session.LastMessageAt,
            exportedAt    = DateTime.UtcNow,
            messageCount  = messages.Count,
            messages      = messages.Select(m => new
            {
                role      = m.Role,
                content   = m.Content,
                timestamp = m.Timestamp
            })
        };

        var json  = JsonSerializer.Serialize(export, IndentedJson);
        var bytes = Encoding.UTF8.GetBytes(json);
        return new ConversationExportResult(bytes, "application/json", $"conversation-{session.Id}.json");
    }

    private static ConversationExportResult FormatMarkdown(ConversationSession session, List<ChatMessage> messages)
    {
        var sb    = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(session.Title) ? session.Id.ToString() : session.Title;

        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine($"**Session ID:** {session.Id}");
        sb.AppendLine($"**Created:** {session.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"**Last Message:** {session.LastMessageAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"**Exported:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        foreach (var msg in messages)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            var roleLabel = msg.Role == "user" ? "User" : "Assistant";
            sb.AppendLine($"### {roleLabel} — {msg.Timestamp:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }

        if (messages.Count > 0)
            sb.AppendLine("---");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new ConversationExportResult(bytes, "text/markdown", $"conversation-{session.Id}.md");
    }

    private static ConversationExportResult FormatText(ConversationSession session, List<ChatMessage> messages)
    {
        var sb    = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(session.Title) ? session.Id.ToString() : session.Title;

        sb.AppendLine($"Conversation: {title}");
        sb.AppendLine($"Session ID: {session.Id}");
        sb.AppendLine($"Created: {session.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Last Message: {session.LastMessageAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine("---");

        foreach (var msg in messages)
        {
            var roleLabel = msg.Role == "user" ? "User" : "Assistant";
            sb.AppendLine();
            sb.AppendLine($"[{roleLabel} - {msg.Timestamp:yyyy-MM-dd HH:mm} UTC]");
            sb.AppendLine(msg.Content);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new ConversationExportResult(bytes, "text/plain", $"conversation-{session.Id}.txt");
    }
}

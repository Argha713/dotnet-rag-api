using RagApi.Domain.Entities;

namespace RagApi.Application.Models;

// Argha - 2026-02-19 - Streaming event model for SSE chat responses 

/// <summary>
/// Represents a single event in a streaming chat response
/// </summary>
public record StreamEvent
{
    /// <summary>
    /// Event type: "sources", "token", or "done"
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Token content — populated for "token" events only
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Source citations — populated for "sources" events only
    /// </summary>
    public List<SourceCitation>? Sources { get; init; }

    /// <summary>
    /// AI model name — populated for "sources" events only
    /// </summary>
    public string? Model { get; init; }
}

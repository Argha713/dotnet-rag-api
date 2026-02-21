using System.ComponentModel.DataAnnotations;

namespace RagApi.Api.Models;

/// <summary>
/// Request model for chat endpoint
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The question or query to answer
    /// </summary>
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Number of document chunks to retrieve (default: 5)
    /// </summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Optional: Filter results to a specific document
    /// </summary>
    public Guid? DocumentId { get; set; }

    /// <summary>
    /// Optional: Previous conversation messages for context
    /// </summary>
    public List<ConversationMessage>? ConversationHistory { get; set; }

    /// <summary>
    /// Optional: Server-side session ID. When provided, history is loaded from the session
    /// and the result is automatically appended. Takes precedence over ConversationHistory.
    /// </summary>
    // Argha - 2026-02-19 - Server-side session support 
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Optional: Filter retrieved chunks to documents tagged with ALL specified tags
    /// </summary>
    // Argha - 2026-02-19 - Tag-based filtering for RAG retrieval 
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional: Override the global hybrid search setting for this request.
    /// true = always hybrid, false = always semantic-only, null = use appsettings default.
    /// </summary>
    // Argha - 2026-02-20 - Per-request hybrid search override 
    public bool? UseHybridSearch { get; set; }

    /// <summary>
    /// Optional: Override the global MMR re-ranking setting for this request.
    /// true = always re-rank, false = skip re-ranking, null = use appsettings default.
    /// </summary>
    // Argha - 2026-02-20 - Per-request MMR re-ranking override 
    public bool? UseReRanking { get; set; }
}

/// <summary>
/// A message in the conversation history
/// </summary>
public class ConversationMessage
{
    /// <summary>
    /// Role: "user" or "assistant"
    /// </summary>
    [Required]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Message content
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Response model for chat endpoint
/// </summary>
public class ChatResponseDto
{
    /// <summary>
    /// The AI-generated answer
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Source documents cited in the answer
    /// </summary>
    public List<SourceDto> Sources { get; set; } = new();

    /// <summary>
    /// The AI model used
    /// </summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Source citation in a chat response
/// </summary>
public class SourceDto
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelevantText { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
}

/// <summary>
/// Request model for search endpoint
/// </summary>
public class SearchRequest
{
    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 50)]
    public int TopK { get; set; } = 5;

    public Guid? DocumentId { get; set; }

    /// <summary>
    /// Optional: Filter results to documents tagged with ALL specified tags
    /// </summary>
    // Argha - 2026-02-19 - Tag-based filtering for semantic search 
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional: Override the global hybrid search setting for this request.
    /// </summary>
    // Argha - 2026-02-20 - Per-request hybrid search override 
    public bool? UseHybridSearch { get; set; }

    /// <summary>
    /// Optional: Override the global MMR re-ranking setting for this request.
    /// </summary>
    // Argha - 2026-02-20 - Per-request MMR re-ranking override 
    public bool? UseReRanking { get; set; }
}

/// <summary>
/// Response model for document listing
/// </summary>
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
    // Argha - 2026-02-19 - Tags deserialized from TagsJson for API response 
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Response model for search results
/// </summary>
public class SearchResultDto
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public int ChunkIndex { get; set; }
}

// Argha - 2026-02-19 - Conversation session DTOs 

/// <summary>Response returned when a new session is created</summary>
public class CreateSessionResponse
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A single message in a session's history</summary>
public class SessionMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>Full session details including message history</summary>
public class SessionDto
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? Title { get; set; }
    public List<SessionMessageDto> Messages { get; set; } = new();
}

/// <summary>
/// Response model for system health/stats
/// </summary>
public class SystemStatsDto
{
    public int TotalDocuments { get; set; }
    public long TotalVectors { get; set; }
    public string AiProvider { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public string ChatModel { get; set; } = string.Empty;
    public int EmbeddingDimension { get; set; }
}

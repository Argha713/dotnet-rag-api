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

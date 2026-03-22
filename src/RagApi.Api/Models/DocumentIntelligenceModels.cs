using System.ComponentModel.DataAnnotations;
using RagApi.Application.Interfaces;

namespace RagApi.Api.Models;

// Argha - 2026-03-22 - #74 - Request body for POST /api/documents/ingest-url
public class IngestUrlRequest
{
    [Required]
    public string Url { get; set; } = string.Empty;

    public List<string>? Tags { get; set; }

    public string? ChunkingStrategy { get; set; }
}

// Argha - 2026-03-22 - #79 - Response for GET /api/documents/{id}/summary
public class DocumentSummaryDto
{
    public Guid DocumentId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime? GeneratedAt { get; set; }
}

// Argha - 2026-03-22 - #80 - Response for GET /api/documents/{id}/tags/suggestions
public class TagSuggestionsDto
{
    public Guid DocumentId { get; set; }
    public List<string> Tags { get; set; } = new();
}

// Argha - 2026-03-22 - #80 - Request body for PUT /api/documents/{id}/tags
public class SetTagsRequest
{
    [Required]
    public List<string> Tags { get; set; } = new();
}

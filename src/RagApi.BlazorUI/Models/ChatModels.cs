namespace RagApi.BlazorUI.Models;

// Argha - 2026-02-21 - Request/response models mirroring RagApi.Api.Models for HTTP calls 

public class ChatRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
    public Guid? SessionId { get; set; }
    public List<string>? Tags { get; set; }
}

public class ChatResponseDto
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceCitationDto> Sources { get; set; } = new();
    public string Model { get; set; } = string.Empty;
}

public class SourceCitationDto
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelevantText { get; set; } = string.Empty;
    public double RelevanceScore { get; set; }
    public int ChunkIndex { get; set; }
}

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}

public class SearchResultDto
{
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public int ChunkIndex { get; set; }
}

// Argha - 2026-02-21 - SSE event model matching the server-side StreamEvent record
public class UiStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public List<SourceCitationDto>? Sources { get; set; }
    public string? Model { get; set; }
}

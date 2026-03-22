using System.ComponentModel.DataAnnotations;

namespace RagApi.Api.Models;

// Argha - 2026-03-22 - #65 - Request body for POST /api/agent/query and POST /api/agent/stream
public class AgentQueryRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;
}

// Argha - 2026-03-22 - #65 - DTO for a single step in the agent reasoning trace
public class AgentStepDto
{
    public int StepNumber { get; set; }
    public string StepType { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? ToolOutput { get; set; }
}

// Argha - 2026-03-22 - #65 - DTO for the full reasoning trace
public class AgentTraceDto
{
    public List<AgentStepDto> Steps { get; set; } = new();
}

// Argha - 2026-03-22 - #65 - DTO for the complete agent response
public class AgentResponseDto
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceDto> Sources { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public AgentTraceDto Trace { get; set; } = new();
    public int IterationsUsed { get; set; }
    public bool MaxIterationsReached { get; set; }
}

// Argha - 2026-03-22 - #65 - SSE event emitted during streaming agent runs
public class AgentStreamEvent
{
    public string Type { get; set; } = string.Empty;
    public AgentStepDto? Step { get; set; }

    // Argha - 2026-03-22 - #65 - Populated on type="done" with the final answer and sources
    public string? Answer { get; set; }
    public List<SourceDto>? Sources { get; set; }
    public string? Model { get; set; }
    public int? IterationsUsed { get; set; }
    public bool? MaxIterationsReached { get; set; }
}

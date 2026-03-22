namespace RagApi.Domain.Entities;

// Argha - 2026-03-22 - #65 - Single step in the agent's ReAct reasoning loop
public class AgentStep
{
    public int StepNumber { get; set; }

    // Argha - 2026-03-22 - #65 - Types: "reasoning", "tool_call", "tool_result", "answer"
    public string StepType { get; set; } = string.Empty;

    public string? Reasoning { get; set; }
    public string? ToolName { get; set; }
    public string? ToolInput { get; set; }
    public string? ToolOutput { get; set; }
}

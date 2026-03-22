namespace RagApi.Application.Models;

// Argha - 2026-03-22 - #65 - Configuration for the Agentic RAG feature
public class AgentConfiguration
{
    public const string SectionName = "Agent";

    // Argha - 2026-03-22 - #65 - Maximum ReAct loop iterations before forcing a final answer
    public int MaxIterations { get; set; } = 5;

    // Argha - 2026-03-22 - #65 - Kill switch: false returns 503 from /api/agent/* endpoints
    public bool Enabled { get; set; } = true;
}

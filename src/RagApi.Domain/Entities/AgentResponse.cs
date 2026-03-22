namespace RagApi.Domain.Entities;

// Argha - 2026-03-22 - #65 - Final response from an agent run, includes reasoning trace
public class AgentResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SourceCitation> Sources { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public AgentTrace Trace { get; set; } = new();
    public int IterationsUsed { get; set; }

    // Argha - 2026-03-22 - #65 - True when agent hit MaxIterations without calling answer_directly
    public bool MaxIterationsReached { get; set; }
}

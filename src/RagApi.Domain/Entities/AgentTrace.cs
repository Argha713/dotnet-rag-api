namespace RagApi.Domain.Entities;

// Argha - 2026-03-22 - #65 - Full log of all steps in one agent run
public class AgentTrace
{
    public List<AgentStep> Steps { get; set; } = new();
}

namespace RagApi.Domain.Entities;

// Argha - 2026-03-22 - #85 - Query type for audit log categorisation
public enum QueryType
{
    Chat,
    Search,
    Agent
}

// Argha - 2026-03-22 - #85 - Audit record for every non-streaming query; one row per request
public class QueryLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public QueryType QueryType { get; set; }
    public string QueryText { get; set; } = string.Empty;   // max 500 chars, truncated before persist
    public string? Model { get; set; }                      // null for Search queries
    public int TokensUsed { get; set; }                     // always 0 in Phase 13
    public decimal EstimatedCostUsd { get; set; }
    public long LatencyMs { get; set; }
    public int SourceCount { get; set; }
    public bool IsSuccess { get; set; } = true;
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}

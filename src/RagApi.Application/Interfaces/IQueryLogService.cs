using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-03-22 - #85 - Fail-open wrapper; never throws; truncates query text; persists log
public interface IQueryLogService
{
    Task LogAsync(QueryLogEntry entry, CancellationToken cancellationToken = default);
}

// Argha - 2026-03-22 - #85 - Input record for logging a query; passed from controllers
public record QueryLogEntry(
    Guid WorkspaceId,
    QueryType QueryType,
    string QueryText,
    string? Model,
    int TokensUsed,
    long LatencyMs,
    int SourceCount,
    bool IsSuccess);

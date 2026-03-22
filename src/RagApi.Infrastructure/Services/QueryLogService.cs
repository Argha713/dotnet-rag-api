using Microsoft.Extensions.Logging;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Services;

// Argha - 2026-03-22 - #85 - Fail-open wrapper: a log failure never propagates to the user
public class QueryLogService : IQueryLogService
{
    private readonly IQueryLogRepository _repository;
    private readonly ICostEstimationService _costEstimation;
    private readonly ILogger<QueryLogService> _logger;

    private const int MaxQueryTextLength = 500;

    public QueryLogService(
        IQueryLogRepository repository,
        ICostEstimationService costEstimation,
        ILogger<QueryLogService> logger)
    {
        _repository = repository;
        _costEstimation = costEstimation;
        _logger = logger;
    }

    public async Task LogAsync(QueryLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            // Argha - 2026-03-22 - #85 - Truncate to keep Neon free-tier rows small
            var queryText = entry.QueryText.Length > MaxQueryTextLength
                ? entry.QueryText[..MaxQueryTextLength]
                : entry.QueryText;

            var cost = await _costEstimation.EstimateCostAsync(entry.Model, entry.TokensUsed);

            var log = new QueryLog
            {
                WorkspaceId = entry.WorkspaceId,
                QueryType = entry.QueryType,
                QueryText = queryText,
                Model = entry.Model,
                TokensUsed = entry.TokensUsed,
                EstimatedCostUsd = cost,
                LatencyMs = entry.LatencyMs,
                SourceCount = entry.SourceCount,
                IsSuccess = entry.IsSuccess
            };

            await _repository.AddAsync(log, cancellationToken);
        }
        catch (Exception ex)
        {
            // Argha - 2026-03-22 - #85 - Logging failure must never break the user-facing query response
            _logger.LogWarning(ex, "Failed to persist query log for workspace {WorkspaceId}", entry.WorkspaceId);
        }
    }
}

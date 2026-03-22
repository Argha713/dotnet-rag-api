using Microsoft.EntityFrameworkCore;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-03-22 - #85 - EF Core implementation of IQueryLogRepository
public class QueryLogRepository : IQueryLogRepository
{
    private readonly RagApiDbContext _dbContext;

    public QueryLogRepository(RagApiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(QueryLog log, CancellationToken cancellationToken = default)
    {
        _dbContext.QueryLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AnalyticsDto> GetAnalyticsAsync(
        Guid workspaceId, int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);

        var rows = await _dbContext.QueryLogs
            .Where(q => q.WorkspaceId == workspaceId && q.LoggedAt >= cutoff)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new AnalyticsDto { DailyBreakdown = new List<DailyAnalyticsDto>() };
        }

        // Argha - 2026-03-22 - #85 - Aggregate totals in memory; dataset is bounded by days window
        var dto = new AnalyticsDto
        {
            TotalQueries = rows.Count,
            TotalCostUsd = rows.Sum(r => r.EstimatedCostUsd),
            AvgLatencyMs = rows.Average(r => (double)r.LatencyMs),
            TotalTokens = rows.Sum(r => r.TokensUsed),
            DailyBreakdown = rows
                .GroupBy(r => DateOnly.FromDateTime(r.LoggedAt))
                .OrderBy(g => g.Key)
                .Select(g => new DailyAnalyticsDto
                {
                    Date = g.Key,
                    QueryCount = g.Count(),
                    CostUsd = g.Sum(r => r.EstimatedCostUsd),
                    AvgLatencyMs = g.Average(r => (double)r.LatencyMs),
                    Tokens = g.Sum(r => r.TokensUsed)
                })
                .ToList()
        };

        return dto;
    }
}

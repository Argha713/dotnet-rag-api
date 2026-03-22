using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-03-22 - #85 - Persistence and aggregation for QueryLog rows
public interface IQueryLogRepository
{
    Task AddAsync(QueryLog log, CancellationToken cancellationToken = default);
    Task<AnalyticsDto> GetAnalyticsAsync(Guid workspaceId, int days, CancellationToken cancellationToken = default);
}

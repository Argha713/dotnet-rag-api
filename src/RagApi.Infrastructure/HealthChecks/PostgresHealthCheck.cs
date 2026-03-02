// Argha - 2026-03-02 - #6 - PostgreSQL health check replacing SqliteHealthCheck (Phase 8)
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RagApi.Infrastructure.Data;

namespace RagApi.Infrastructure.HealthChecks;

/// <summary>
/// Health check for PostgreSQL database connectivity
/// </summary>
public class PostgresHealthCheck : IHealthCheck
{
    private readonly RagApiDbContext _dbContext;
    private readonly ILogger<PostgresHealthCheck> _logger;

    public PostgresHealthCheck(
        RagApiDbContext dbContext,
        ILogger<PostgresHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("PostgreSQL database reachable");
            }
            return HealthCheckResult.Unhealthy("PostgreSQL database cannot connect");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgreSQL health check failed");
            return HealthCheckResult.Unhealthy("PostgreSQL database error", ex);
        }
    }
}

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RagApi.Infrastructure.Data;

namespace RagApi.Infrastructure.HealthChecks;

/// <summary>
/// Health check for SQLite database connectivity
/// </summary>
public class SqliteHealthCheck : IHealthCheck
{
    private readonly RagApiDbContext _dbContext;
    private readonly ILogger<SqliteHealthCheck> _logger;

    public SqliteHealthCheck(
        RagApiDbContext dbContext,
        ILogger<SqliteHealthCheck> logger)
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
                return HealthCheckResult.Healthy("SQLite database reachable");
            }
            return HealthCheckResult.Unhealthy("SQLite database cannot connect");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQLite health check failed");
            return HealthCheckResult.Unhealthy("SQLite database error", ex);
        }
    }
}

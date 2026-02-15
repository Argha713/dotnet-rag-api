using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace RagApi.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Qdrant vector database connectivity
/// </summary>
public class QdrantHealthCheck : IHealthCheck
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantHealthCheck> _logger;

    public QdrantHealthCheck(
        IOptions<QdrantConfiguration> config,
        ILogger<QdrantHealthCheck> logger)
    {
        var cfg = config.Value;
        _client = new QdrantClient(
            host: cfg.Host,
            port: cfg.Port,
            https: cfg.UseTls,
            apiKey: cfg.ApiKey);
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["collections"] = collections.Count
            };
            return HealthCheckResult.Healthy(
                $"Qdrant reachable — {collections.Count} collection(s)", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant health check failed");
            return HealthCheckResult.Unhealthy("Qdrant unreachable", ex);
        }
    }
}

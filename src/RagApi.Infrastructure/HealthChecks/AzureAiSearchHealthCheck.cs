// Argha - 2026-02-21 - Health check for Azure AI Search (Phase 5.1)
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Infrastructure.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to Azure AI Search by fetching index statistics.
/// Registered automatically when VectorStore:Provider = "AzureAiSearch".
/// </summary>
public class AzureAiSearchHealthCheck : IHealthCheck
{
    private readonly SearchIndexClient _indexClient;
    private readonly AzureAiSearchSettings _settings;
    private readonly ILogger<AzureAiSearchHealthCheck> _logger;

    public AzureAiSearchHealthCheck(
        SearchIndexClient indexClient,
        IOptions<VectorStoreConfiguration> config,
        ILogger<AzureAiSearchHealthCheck> logger)
    {
        _indexClient = indexClient;
        _settings = config.Value.AzureAiSearch;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Argha - 2026-02-21 - GetIndexStatisticsAsync is lightweight and confirms auth + connectivity
            await _indexClient.GetIndexStatisticsAsync(_settings.IndexName, cancellationToken);
            return HealthCheckResult.Healthy($"Azure AI Search reachable, index: {_settings.IndexName}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure AI Search health check failed for index '{IndexName}'", _settings.IndexName);
            return HealthCheckResult.Unhealthy(
                $"Azure AI Search unreachable: {ex.Message}",
                exception: ex);
        }
    }
}

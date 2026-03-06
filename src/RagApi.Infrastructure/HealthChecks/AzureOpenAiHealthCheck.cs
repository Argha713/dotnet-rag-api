using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Infrastructure.HealthChecks;

// Argha - 2026-03-07 - #22 - Health check for Azure OpenAI service connectivity
public class AzureOpenAiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAiSettings _settings;
    private readonly ILogger<AzureOpenAiHealthCheck> _logger;

    public AzureOpenAiHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<AiConfiguration> config,
        ILogger<AzureOpenAiHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = config.Value.AzureOpenAI;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);

            var response = await client.GetAsync(
                $"{_settings.Endpoint}/openai/models?api-version=2024-02-01", cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = new Dictionary<string, object>
            {
                ["provider"] = "AzureOpenAI",
                ["endpoint"] = _settings.Endpoint,
                ["chatDeployment"] = _settings.ChatDeployment,
                ["embeddingDeployment"] = _settings.EmbeddingDeployment
            };
            return HealthCheckResult.Healthy("Azure OpenAI reachable", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure OpenAI health check failed");
            return HealthCheckResult.Unhealthy("Azure OpenAI unreachable", ex);
        }
    }
}

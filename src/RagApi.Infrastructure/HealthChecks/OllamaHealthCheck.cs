using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Ollama AI service connectivity
/// </summary>
public class OllamaHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaSettings _settings;
    private readonly ILogger<OllamaHealthCheck> _logger;

    public OllamaHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<AiConfiguration> config,
        ILogger<OllamaHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = config.Value.Ollama;
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

            var response = await client.GetAsync(
                $"{_settings.BaseUrl}/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = new Dictionary<string, object>
            {
                ["baseUrl"] = _settings.BaseUrl,
                ["chatModel"] = _settings.ChatModel,
                ["embeddingModel"] = _settings.EmbeddingModel
            };
            return HealthCheckResult.Healthy("Ollama reachable", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health check failed");
            return HealthCheckResult.Unhealthy("Ollama unreachable", ex);
        }
    }
}

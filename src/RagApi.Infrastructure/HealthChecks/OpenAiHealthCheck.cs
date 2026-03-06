using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Infrastructure.HealthChecks;

// Argha - 2026-03-07 - #22 - Health check for OpenAI API connectivity
public class OpenAiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiHealthCheck> _logger;

    public OpenAiHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<AiConfiguration> config,
        ILogger<OpenAiHealthCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = config.Value.OpenAi;
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
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var response = await client.GetAsync(
                $"{_settings.BaseUrl}/models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = new Dictionary<string, object>
            {
                ["provider"] = "OpenAI",
                ["chatModel"] = _settings.ChatModel,
                ["embeddingModel"] = _settings.EmbeddingModel
            };
            return HealthCheckResult.Healthy("OpenAI reachable", data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI health check failed");
            return HealthCheckResult.Unhealthy("OpenAI unreachable", ex);
        }
    }
}

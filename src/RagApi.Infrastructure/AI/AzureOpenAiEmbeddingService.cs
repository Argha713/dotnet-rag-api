using RagApi.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RagApi.Infrastructure.AI;

/// <summary>
/// Embedding service implementation using Azure OpenAI
/// </summary>
public class AzureOpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiSettings _settings;
    private readonly ILogger<AzureOpenAiEmbeddingService> _logger;

    public AzureOpenAiEmbeddingService(
        HttpClient httpClient,
        IOptions<AiConfiguration> options,
        ILogger<AzureOpenAiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.AzureOpenAI;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri(_settings.Endpoint);
        _httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public int EmbeddingDimension => _settings.EmbeddingDimension;
    public string ModelName => _settings.EmbeddingDeployment;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync(new List<string> { text }, cancellationToken);
        return embeddings.First();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        var request = new AzureEmbeddingRequest
        {
            Input = texts
        };

        var url = $"/openai/deployments/{_settings.EmbeddingDeployment}/embeddings?api-version=2024-02-01";

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AzureEmbeddingResponse>(cancellationToken: cancellationToken);
            
            if (result?.Data == null || result.Data.Count == 0)
            {
                throw new InvalidOperationException("Azure OpenAI returned empty embeddings");
            }

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings with Azure OpenAI");
            throw;
        }
    }
}

internal class AzureEmbeddingRequest
{
    [JsonPropertyName("input")]
    public List<string> Input { get; set; } = new();
}

internal class AzureEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<AzureEmbeddingData> Data { get; set; } = new();
}

internal class AzureEmbeddingData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }
    
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

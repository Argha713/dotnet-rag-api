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
        // Argha - 2026-03-23 - #96 - Batch to stay under Azure OpenAI's 2048-input-per-request limit and avoid timeouts on large PDFs
        var batchSize = _settings.EmbeddingBatchSize;
        var allEmbeddings = new List<float[]>(texts.Count);
        var url = $"/openai/deployments/{_settings.EmbeddingDeployment}/embeddings?api-version=2024-02-01";

        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.GetRange(i, Math.Min(batchSize, texts.Count - i));
            _logger.LogDebug("Embedding batch {BatchStart}-{BatchEnd} of {Total}", i + 1, i + batch.Count, texts.Count);

            var request = new AzureEmbeddingRequest { Input = batch };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<AzureEmbeddingResponse>(cancellationToken: cancellationToken);

                if (result?.Data == null || result.Data.Count == 0)
                    throw new InvalidOperationException($"Azure OpenAI returned empty embeddings for batch starting at index {i}");

                allEmbeddings.AddRange(result.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embeddings with Azure OpenAI for batch starting at index {StartIndex}", i);
                throw;
            }
        }

        return allEmbeddings;
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

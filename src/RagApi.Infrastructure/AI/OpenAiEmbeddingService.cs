// Argha - 2026-03-01 - OpenAI direct API embedding service (Phase 7)
using RagApi.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace RagApi.Infrastructure.AI;

/// <summary>
/// Embedding service implementation using the OpenAI API directly (text-embedding-3-small etc.)
/// </summary>
public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        HttpClient httpClient,
        IOptions<AiConfiguration> options,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.OpenAi;
        _logger = logger;

        // Argha - 2026-03-01 - OpenAI uses Bearer token auth
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public int EmbeddingDimension => _settings.EmbeddingDimension;
    public string ModelName => _settings.EmbeddingModel;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embeddings = await GenerateEmbeddingsAsync(new List<string> { text }, cancellationToken);
        return embeddings.First();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-23 - #96 - Batch to stay under OpenAI's 2048-input-per-request limit and avoid timeouts on large PDFs
        var batchSize = _settings.EmbeddingBatchSize;
        var allEmbeddings = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i += batchSize)
        {
            var batch = texts.GetRange(i, Math.Min(batchSize, texts.Count - i));
            _logger.LogDebug("Embedding batch {BatchStart}-{BatchEnd} of {Total}", i + 1, i + batch.Count, texts.Count);

            var request = new OpenAiEmbeddingRequest { Input = batch, Model = _settings.EmbeddingModel };

            try
            {
                var response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken);

                if (result?.Data == null || result.Data.Count == 0)
                    throw new InvalidOperationException($"OpenAI returned empty embeddings for batch starting at index {i}");

                allEmbeddings.AddRange(result.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embeddings with OpenAI for batch starting at index {StartIndex}", i);
                throw;
            }
        }

        return allEmbeddings;
    }
}

internal class OpenAiEmbeddingRequest
{
    [JsonPropertyName("input")]
    public List<string> Input { get; set; } = new();

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;
}

internal class OpenAiEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingData> Data { get; set; } = new();
}

internal class OpenAiEmbeddingData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

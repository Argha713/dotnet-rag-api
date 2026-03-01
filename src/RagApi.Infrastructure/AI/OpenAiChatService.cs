// Argha - 2026-03-01 - OpenAI direct API chat service (Phase 7)
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagApi.Infrastructure.AI;

/// <summary>
/// Chat service implementation using the OpenAI API directly (gpt-4o-mini etc.)
/// </summary>
public class OpenAiChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _settings;
    private readonly ILogger<OpenAiChatService> _logger;

    public OpenAiChatService(
        HttpClient httpClient,
        IOptions<AiConfiguration> options,
        ILogger<OpenAiChatService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.OpenAi;
        _logger = logger;

        // Argha - 2026-03-01 - OpenAI uses Bearer token auth, not Azure's api-key header
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public string ModelName => _settings.ChatModel;

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var openAiMessages = new List<OpenAiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        openAiMessages.AddRange(messages.Select(m => new OpenAiChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new OpenAiChatRequest
        {
            Model = _settings.ChatModel,
            Messages = openAiMessages,
            MaxTokens = 4096,
            Temperature = 0.7f
        };

        try
        {
            // Argha - 2026-03-01 - Standard OpenAI chat completions endpoint (no deployment in URL)
            var response = await _httpClient.PostAsJsonAsync("chat/completions", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken);

            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate chat response with OpenAI");
            throw;
        }
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openAiMessages = new List<OpenAiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        openAiMessages.AddRange(messages.Select(m => new OpenAiChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new OpenAiChatRequest
        {
            Model = _settings.ChatModel,
            Messages = openAiMessages,
            MaxTokens = 4096,
            Temperature = 0.7f,
            Stream = true
        };

        var jsonContent = JsonSerializer.Serialize(request);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = httpContent
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Argha - 2026-03-01 - SSE format: "data: {...}" lines, ends with "data: [DONE]"
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line.Substring(6);
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAiChatResponse>(data);
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content != null)
            {
                yield return content;
            }
        }
    }
}

internal class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChatChoice>? Choices { get; set; }
}

internal class OpenAiChatChoice
{
    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiChatMessage? Delta { get; set; }
}

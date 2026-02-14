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
/// Chat service implementation using Ollama
/// </summary>
public class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;
    private readonly ILogger<OllamaChatService> _logger;

    public OllamaChatService(
        HttpClient httpClient,
        IOptions<AiConfiguration> options,
        ILogger<OllamaChatService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Ollama;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public string ModelName => _settings.ChatModel;

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var ollamaMessages = new List<OllamaChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        
        ollamaMessages.AddRange(messages.Select(m => new OllamaChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new OllamaChatRequest
        {
            Model = _settings.ChatModel,
            Messages = ollamaMessages,
            Stream = false
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
            
            return result?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate chat response with Ollama");
            throw;
        }
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ollamaMessages = new List<OllamaChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        
        ollamaMessages.AddRange(messages.Select(m => new OllamaChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new OllamaChatRequest
        {
            Model = _settings.ChatModel,
            Messages = ollamaMessages,
            Stream = true
        };

        var jsonContent = JsonSerializer.Serialize(request);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
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

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
            if (chunk?.Message?.Content != null)
            {
                yield return chunk.Message.Content;
            }
        }
    }
}

internal class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;
    
    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; set; } = new();
    
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaChatMessage? Message { get; set; }
    
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

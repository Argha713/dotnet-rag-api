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
/// Chat service implementation using Azure OpenAI
/// </summary>
public class AzureOpenAiChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiSettings _settings;
    private readonly ILogger<AzureOpenAiChatService> _logger;

    public AzureOpenAiChatService(
        HttpClient httpClient,
        IOptions<AiConfiguration> options,
        ILogger<AzureOpenAiChatService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.AzureOpenAI;
        _logger = logger;
        
        _httpClient.BaseAddress = new Uri(_settings.Endpoint);
        _httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public string ModelName => _settings.ChatDeployment;

    public async Task<string> GenerateResponseAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var azureMessages = new List<AzureChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        
        azureMessages.AddRange(messages.Select(m => new AzureChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new AzureChatRequest
        {
            Messages = azureMessages,
            MaxTokens = 4096,
            Temperature = 0.7f
        };

        var url = $"/openai/deployments/{_settings.ChatDeployment}/chat/completions?api-version=2024-02-01";

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AzureChatResponse>(cancellationToken: cancellationToken);
            
            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate chat response with Azure OpenAI");
            throw;
        }
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var azureMessages = new List<AzureChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        
        azureMessages.AddRange(messages.Select(m => new AzureChatMessage
        {
            Role = m.Role,
            Content = m.Content
        }));

        var request = new AzureChatRequest
        {
            Messages = azureMessages,
            MaxTokens = 4096,
            Temperature = 0.7f,
            Stream = true
        };

        var url = $"/openai/deployments/{_settings.ChatDeployment}/chat/completions?api-version=2024-02-01";
        
        var jsonContent = JsonSerializer.Serialize(request);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
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
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            
            var data = line.Substring(6);
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<AzureChatResponse>(data);
            var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (content != null)
            {
                yield return content;
            }
        }
    }
}

internal class AzureChatRequest
{
    [JsonPropertyName("messages")]
    public List<AzureChatMessage> Messages { get; set; } = new();
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }
    
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }
    
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal class AzureChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class AzureChatResponse
{
    [JsonPropertyName("choices")]
    public List<AzureChatChoice>? Choices { get; set; }
}

internal class AzureChatChoice
{
    [JsonPropertyName("message")]
    public AzureChatMessage? Message { get; set; }
    
    [JsonPropertyName("delta")]
    public AzureChatMessage? Delta { get; set; }
}

// Argha - 2026-03-16 - #32 - GPT-4o vision service: describes images extracted from documents
// for multimodal RAG ingestion. Called by DocumentService (#36) per extracted image.
using RagApi.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagApi.Infrastructure.AI;

public class OpenAIVisionService : IVisionService
{
    private readonly HttpClient _httpClient;
    private readonly VisionConfiguration _visionConfig;
    private readonly ILogger<OpenAIVisionService> _logger;

    // Argha - 2026-03-16 - #32 - Prompt tuned for technical document images (diagrams, UI screenshots,
    // step-numbered figures) — dense output maximises embedding signal for RAG retrieval
    private const string SystemPrompt =
        "You are describing images from a technical document for a search index. " +
        "Be specific about: visible text, labels, step numbers, arrows/indicators, " +
        "diagrams, UI elements. Output a dense descriptive paragraph.";

    public OpenAIVisionService(
        HttpClient httpClient,
        IOptions<AiConfiguration> aiOptions,
        IOptions<VisionConfiguration> visionOptions,
        ILogger<OpenAIVisionService> logger)
    {
        _httpClient = httpClient;
        _visionConfig = visionOptions.Value;
        _logger = logger;

        // Argha - 2026-03-16 - #32 - Reuse same OpenAI base URL and API key as chat/embedding services
        var settings = aiOptions.Value.OpenAi;
        _httpClient.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    // Argha - 2026-03-16 - #32 - Always true when this class is registered;
    // DI only registers OpenAIVisionService when Vision:Enabled=true + Provider=OpenAI
    public bool IsEnabled => true;

    public async Task<string> DescribeImageAsync(
        byte[] imageBytes,
        string mimeType,
        string? context = null,
        CancellationToken ct = default)
    {
        var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";

        var contentParts = new List<VisionContentPart>
        {
            new() { Type = "image_url", ImageUrl = new VisionImageUrl { Url = dataUri } }
        };

        // Argha - 2026-03-16 - #32 - Extra context (e.g. figure caption, document title) added as
        // a text part alongside the image so the model can give a more targeted description
        if (context is not null)
            contentParts.Add(new VisionContentPart { Type = "text", Text = context });

        var request = new VisionRequest
        {
            Model = _visionConfig.Model,
            Messages =
            [
                new() { Role = "system", Content = SystemPrompt },
                new() { Role = "user",   Content = contentParts }
            ],
            MaxTokens = 1024
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("chat/completions", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<VisionResponse>(cancellationToken: ct);

            // Argha - 2026-03-16 - #32 - Log token counts per image for Phase 13 cost tracking
            if (result?.Usage is { } usage)
                _logger.LogInformation(
                    "Vision tokens — prompt: {PromptTokens}, completion: {CompletionTokens}",
                    usage.PromptTokens, usage.CompletionTokens);

            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to describe image with vision model {Model}", _visionConfig.Model);
            throw;
        }
    }
}

// Argha - 2026-03-16 - #32 - Vision-specific DTOs kept separate from OpenAiChatService because
// the user message content is an array of parts (image_url + optional text), not a plain string

internal class VisionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<VisionMessage> Messages { get; set; } = [];

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }
}

internal class VisionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    // Argha - 2026-03-16 - #32 - string for system messages, List<VisionContentPart> for user messages;
    // System.Text.Json serialises the runtime type so both shapes produce correct JSON
    [JsonPropertyName("content")]
    public object Content { get; set; } = string.Empty;
}

internal class VisionContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VisionImageUrl? ImageUrl { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

internal class VisionImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

internal class VisionResponse
{
    [JsonPropertyName("choices")]
    public List<VisionChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public VisionUsage? Usage { get; set; }
}

internal class VisionChoice
{
    [JsonPropertyName("message")]
    public VisionResponseMessage? Message { get; set; }
}

internal class VisionResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal class VisionUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
}

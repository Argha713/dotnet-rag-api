// Argha - 2026-03-22 - #81 - OpenAI Whisper transcription service (Phase 12 audio ingestion)
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace RagApi.Infrastructure.AI;

// Argha - 2026-03-22 - #81 - POSTs multipart/form-data to {BaseUrl}/audio/transcriptions
// and parses the JSON { "text": "..." } response returned by the Whisper API
internal sealed class OpenAiWhisperService : IAudioTranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly AudioTranscriptionOptions _options;
    private readonly ILogger<OpenAiWhisperService> _logger;

    // Argha - 2026-03-22 - #81 - BaseUrl and ApiKey come from the active OpenAI provider settings
    public OpenAiWhisperService(
        HttpClient httpClient,
        IOptions<AiConfiguration> aiOptions,
        IOptions<AudioTranscriptionOptions> transcriptionOptions,
        ILogger<OpenAiWhisperService> logger)
    {
        _httpClient = httpClient;
        _options = transcriptionOptions.Value;
        _logger = logger;

        var settings = aiOptions.Value.OpenAi;
        _httpClient.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ApiKey}");
    }

    public bool IsSupported => true;

    public async Task<string> TranscribeAsync(
        Stream audioStream,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-22 - #81 - Whisper requires multipart/form-data with "file" + "model" fields
        using var form = new MultipartFormDataContent();

        var fileContent = new StreamContent(audioStream);
        var fileName = GetFileName(mimeType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(_options.WhisperModel), "model");

        _logger.LogDebug("Sending audio ({MimeType}, model={Model}) to Whisper API", mimeType, _options.WhisperModel);

        var response = await _httpClient.PostAsync("audio/transcriptions", form, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Whisper API returned {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Whisper API returned {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // Argha - 2026-03-22 - #81 - Response shape: { "text": "transcribed content" }
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("text", out var textElement)
            ? textElement.GetString() ?? string.Empty
            : string.Empty;
    }

    // Argha - 2026-03-22 - #81 - Maps MIME type to a filename extension accepted by Whisper
    private static string GetFileName(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "audio/mpeg" => "audio.mp3",
        "audio/wav" or "audio/wave" or "audio/x-wav" => "audio.wav",
        "audio/mp4" or "audio/m4a" => "audio.mp4",
        "audio/ogg" => "audio.ogg",
        "audio/webm" => "audio.webm",
        _ => "audio.bin"
    };
}

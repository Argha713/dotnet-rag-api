using System.Net.Http.Json;
using System.Text.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-02-21 - HTTP client for /api/chat and /api/chat/search endpoints 
public class ChatApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ChatApiService(HttpClient http)
    {
        _http = http;
    }

    // Argha - 2026-03-17 - #39 - Expose base URL so Chat.razor can construct image src URLs
    // without re-reading IConfiguration; BaseAddress is set by Program.cs from ApiBaseUrl config
    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    public async Task<ChatResponseDto> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/chat", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponseDto>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from /api/chat");
    }

    public async Task<List<SearchResultDto>> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/chat/search", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SearchResultDto>>(_jsonOptions, ct)
            ?? new List<SearchResultDto>();
    }

    // Argha - 2026-02-21 - Consume SSE stream from /api/chat/stream; parse data: lines and invoke callback per event
    public async Task ChatStreamAsync(ChatRequest request, Action<UiStreamEvent> onEvent, CancellationToken ct = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(request, options: _jsonOptions)
        };

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            var evt = JsonSerializer.Deserialize<UiStreamEvent>(data, _jsonOptions);
            if (evt != null)
            {
                onEvent(evt);
                if (evt.Type == "done") break;
            }
        }
    }
}

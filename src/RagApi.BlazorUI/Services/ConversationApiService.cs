using System.Net.Http.Json;
using System.Text.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-02-21 - HTTP client for /api/conversations endpoints 
public class ConversationApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ConversationApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<CreateSessionResponse> CreateAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/api/conversations", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreateSessionResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from create conversation endpoint");
    }

    public async Task<SessionDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<SessionDto>($"/api/conversations/{id}", _jsonOptions, ct);
        return result ?? throw new InvalidOperationException($"Conversation {id} not found");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/conversations/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-03-04 - #17 - HTTP client for /api/workspaces endpoints
public class WorkspaceApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public WorkspaceApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<WorkspaceCreatedDto> CreateAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/workspaces", new { Name = name }, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkspaceCreatedDto>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from /api/workspaces");
    }

    // Argha - 2026-03-04 - #17 - Optional apiKey param: when set, bypasses WorkspaceKeyHandler (import flow)
    public async Task<WorkspaceDto?> GetCurrentAsync(string? apiKey = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces/current");
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        try
        {
            var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<WorkspaceDto>(_jsonOptions, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/workspaces/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    // Argha - 2026-03-04 - #17 - Validates key by calling GET /api/documents; 401 = invalid key
    // Optional apiKey param bypasses WorkspaceKeyHandler for import validation
    public async Task<bool> ValidateKeyAsync(string? apiKey = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        try
        {
            var response = await _http.SendAsync(request, ct);
            return response.StatusCode != System.Net.HttpStatusCode.Unauthorized;
        }
        catch
        {
            return false;
        }
    }
}

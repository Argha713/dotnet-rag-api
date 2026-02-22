using System.Net.Http.Json;
using System.Text.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-02-21 - HTTP client for /api/documents endpoints 
// Note: upload accepts a plain Stream to avoid Blazor-specific IBrowserFile dependency, keeping the service testable
public class DocumentApiService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DocumentApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DocumentDto>> GetDocumentsAsync(string? tag = null, CancellationToken ct = default)
    {
        var url = tag != null
            ? $"/api/documents?tag={Uri.EscapeDataString(tag)}"
            : "/api/documents";
        return await _http.GetFromJsonAsync<List<DocumentDto>>(url, _jsonOptions, ct)
            ?? new List<DocumentDto>();
    }

    // Argha - 2026-02-21 - Takes a Stream so the component handles IBrowserFile; service stays framework-agnostic
    public async Task<DocumentDto> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        List<string> tags,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);
        foreach (var tag in tags)
            content.Add(new StringContent(tag), "tags");

        var response = await _http.PostAsync("/api/documents", content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocumentDto>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from upload endpoint");
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/documents/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<string>> GetSupportedTypesAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<string>>("/api/documents/supported-types", _jsonOptions, ct)
            ?? new List<string>();
    }
}

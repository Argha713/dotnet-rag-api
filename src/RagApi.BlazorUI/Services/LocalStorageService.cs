using Microsoft.JSInterop;
using System.Text.Json;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-02-21 - Thin IJSRuntime wrapper for browser localStorage persistence 
public class LocalStorageService
{
    private readonly IJSRuntime _js;
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SetItemAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value, _options);
        await _js.InvokeVoidAsync("localStorage.setItem", key, json);
    }

    public async Task<T?> GetItemAsync<T>(string key)
    {
        var json = await _js.InvokeAsync<string?>("localStorage.getItem", key);
        if (string.IsNullOrEmpty(json)) return default;
        return JsonSerializer.Deserialize<T>(json, _options);
    }

    public async Task RemoveItemAsync(string key)
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", key);
    }
}

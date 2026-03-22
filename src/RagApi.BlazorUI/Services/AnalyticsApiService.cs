using System.Net.Http.Json;
using RagApi.BlazorUI.Models;

namespace RagApi.BlazorUI.Services;

// Argha - 2026-03-22 - #91 - Fetches analytics data from GET /api/analytics
public class AnalyticsApiService
{
    private readonly HttpClient _http;

    public AnalyticsApiService(HttpClient http)
    {
        _http = http;
    }

    // Argha - 2026-03-22 - #91 - Fail-open: returns null on any error so UI can show error state
    public async Task<AnalyticsDto?> GetAnalyticsAsync(int days = 7, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<AnalyticsDto>(
                $"/api/analytics?days={days}", cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}

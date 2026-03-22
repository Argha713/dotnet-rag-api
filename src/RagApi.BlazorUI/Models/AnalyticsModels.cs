namespace RagApi.BlazorUI.Models;

// Argha - 2026-03-22 - #91 - Mirrors Application.Models.AnalyticsDto for Blazor deserialization
public class AnalyticsDto
{
    public int TotalQueries { get; set; }
    public decimal TotalCostUsd { get; set; }
    public double AvgLatencyMs { get; set; }
    public int TotalTokens { get; set; }
    public List<DailyAnalyticsDto> DailyBreakdown { get; set; } = new();
}

// Argha - 2026-03-22 - #91 - Per-day breakdown entry
public class DailyAnalyticsDto
{
    public DateOnly Date { get; set; }
    public int QueryCount { get; set; }
    public decimal CostUsd { get; set; }
    public double AvgLatencyMs { get; set; }
    public int Tokens { get; set; }
}

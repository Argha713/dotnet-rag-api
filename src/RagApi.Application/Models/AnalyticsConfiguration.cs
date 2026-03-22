namespace RagApi.Application.Models;

// Argha - 2026-03-22 - #85 - Configuration for cost estimation per AI model
public class AnalyticsConfiguration
{
    public const string SectionName = "Analytics";

    // Argha - 2026-03-22 - #85 - Key: model name (case-insensitive). Value: pricing entry.
    public Dictionary<string, ModelPricingEntry> ModelPricing { get; set; } = new();
}

// Argha - 2026-03-22 - #85 - Cost per 1,000 tokens for a given model
public class ModelPricingEntry
{
    public decimal InputPer1kTokens { get; set; }
    public decimal OutputPer1kTokens { get; set; }
}

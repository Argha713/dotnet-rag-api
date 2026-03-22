using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;

namespace RagApi.Infrastructure.Services;

// Argha - 2026-03-22 - #85 - Estimates query cost from a pricing table in appsettings.json
public class CostEstimationService : ICostEstimationService
{
    private readonly Dictionary<string, ModelPricingEntry> _pricing;

    public CostEstimationService(IOptions<AnalyticsConfiguration> options)
    {
        // Argha - 2026-03-22 - #85 - Build case-insensitive dict at construction time; config is read-only
        _pricing = new Dictionary<string, ModelPricingEntry>(
            options.Value.ModelPricing,
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<decimal> EstimateCostAsync(string? modelName, int tokensUsed)
    {
        if (modelName == null || tokensUsed <= 0)
            return Task.FromResult(0m);

        if (!_pricing.TryGetValue(modelName, out var entry))
            return Task.FromResult(0m);

        // Argha - 2026-03-22 - #85 - Single-rate formula; no input/output split (token counts not split in Phase 13)
        var cost = (tokensUsed / 1000m) * entry.InputPer1kTokens;
        return Task.FromResult(cost);
    }
}

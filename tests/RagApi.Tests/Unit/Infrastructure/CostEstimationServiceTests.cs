using FluentAssertions;
using Microsoft.Extensions.Options;
using RagApi.Application.Models;
using RagApi.Infrastructure.Services;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-03-22 - #92 - Unit tests for CostEstimationService
public class CostEstimationServiceTests
{
    private static CostEstimationService CreateService(Dictionary<string, ModelPricingEntry>? pricing = null)
    {
        var config = new AnalyticsConfiguration
        {
            ModelPricing = pricing ?? new Dictionary<string, ModelPricingEntry>
            {
                ["gpt-4o-mini"] = new ModelPricingEntry { InputPer1kTokens = 0.00015m, OutputPer1kTokens = 0.0006m },
                ["gpt-4o"]      = new ModelPricingEntry { InputPer1kTokens = 0.005m,   OutputPer1kTokens = 0.015m }
            }
        };
        return new CostEstimationService(Options.Create(config));
    }

    [Fact]
    public async Task EstimateCostAsync_KnownModel_ReturnsCorrectCost()
    {
        // Argha - 2026-03-22 - #92 - 1000 tokens at $0.00015/1k = $0.00015
        var sut = CreateService();
        var cost = await sut.EstimateCostAsync("gpt-4o-mini", 1000);
        cost.Should().Be(0.00015m);
    }

    [Fact]
    public async Task EstimateCostAsync_UnknownModel_ReturnsZero()
    {
        var sut = CreateService();
        var cost = await sut.EstimateCostAsync("unknown-model", 1000);
        cost.Should().Be(0m);
    }

    [Fact]
    public async Task EstimateCostAsync_NullModel_ReturnsZero()
    {
        var sut = CreateService();
        var cost = await sut.EstimateCostAsync(null, 1000);
        cost.Should().Be(0m);
    }

    [Fact]
    public async Task EstimateCostAsync_ZeroTokens_ReturnsZero()
    {
        var sut = CreateService();
        var cost = await sut.EstimateCostAsync("gpt-4o-mini", 0);
        cost.Should().Be(0m);
    }

    [Fact]
    public async Task EstimateCostAsync_CaseInsensitive_FindsModel()
    {
        // Argha - 2026-03-22 - #92 - Config key is lowercase; query uses uppercase
        var sut = CreateService();
        var cost = await sut.EstimateCostAsync("GPT-4O-MINI", 1000);
        cost.Should().Be(0.00015m);
    }
}

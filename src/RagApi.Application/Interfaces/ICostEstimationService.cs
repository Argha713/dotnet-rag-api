namespace RagApi.Application.Interfaces;

// Argha - 2026-03-22 - #85 - Estimates query cost from model name + token count
public interface ICostEstimationService
{
    Task<decimal> EstimateCostAsync(string? modelName, int tokensUsed);
}

namespace RagApi.Application.Models;

/// <summary>
/// Configuration options for search behaviour (hybrid search, re-ranking)
/// </summary>
// Argha - 2026-02-20 - Config POCO for Phase 3 search features (Phase 3.1)
public class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>
    /// When true, combines vector similarity search with full-text keyword search using RRF fusion.
    /// Can be overridden per-request via the UseHybridSearch field.
    /// </summary>
    public bool UseHybridSearch { get; set; } = false;

    /// <summary>
    /// When true, applies Maximal Marginal Relevance re-ranking after retrieval.
    /// Can be overridden per-request via the UseReRanking field.
    /// </summary>
    public bool UseReRanking { get; set; } = false;

    /// <summary>
    /// Lambda parameter for MMR: 1.0 = pure relevance, 0.0 = pure diversity. Default 0.5.
    /// </summary>
    public double MmrLambda { get; set; } = 0.5;

    /// <summary>
    /// Multiplier applied to topK when fetching candidates for hybrid search or re-ranking.
    /// E.g. topK=5, multiplier=3 → fetch 15 candidates before fusion/re-ranking.
    /// </summary>
    public int CandidateMultiplier { get; set; } = 3;
}

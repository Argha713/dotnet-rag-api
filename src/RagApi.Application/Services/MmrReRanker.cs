using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

/// <summary>
/// Implements Maximal Marginal Relevance (MMR) re-ranking to reduce redundancy in search results.
/// </summary>
// Argha - 2026-02-20 - MMR re-ranking
public static class MmrReRanker
{
    /// <summary>
    /// Re-ranks <paramref name="candidates"/> using MMR so results are both relevant and diverse.
    /// </summary>
    /// <param name="candidates">Candidate results with their Embedding populated.</param>
    /// <param name="queryEmbedding">The original query embedding.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="lambda">
    /// Trade-off parameter: 1.0 = pure relevance (sorted by score), 0.0 = pure diversity.
    /// Default 0.5 balances both.
    /// </param>
    public static List<SearchResult> Rerank(
        List<SearchResult> candidates,
        float[] queryEmbedding,
        int topK,
        double lambda = 0.5)
    {
        if (candidates.Count == 0)
            return new List<SearchResult>();

        topK = Math.Min(topK, candidates.Count);

        var remaining = candidates.ToList();
        var selected = new List<SearchResult>(topK);

        while (selected.Count < topK && remaining.Count > 0)
        {
            SearchResult? best = null;
            var bestScore = double.NegativeInfinity;

            foreach (var candidate in remaining)
            {
                // Argha - 2026-02-20 - Use stored score as query similarity proxy when embedding is absent 
                var querySim = candidate.Embedding != null
                    ? CosineSimilarity(candidate.Embedding, queryEmbedding)
                    : candidate.Score;

                // Argha - 2026-02-20 - Maximum similarity to any already-selected result 
                var maxSelectedSim = selected.Count == 0
                    ? 0.0
                    : selected.Max(s =>
                        candidate.Embedding != null && s.Embedding != null
                            ? CosineSimilarity(candidate.Embedding, s.Embedding)
                            : 0.0);

                var mmrScore = lambda * querySim - (1.0 - lambda) * maxSelectedSim;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    best = candidate;
                }
            }

            if (best == null) break;

            selected.Add(best);
            remaining.Remove(best);
        }

        return selected;
    }

    // Argha - 2026-02-20 - Standard cosine similarity: dot product / (|a| * |b|) 
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return 0.0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom < 1e-10 ? 0.0 : dot / denom;
    }
}

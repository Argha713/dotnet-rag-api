using FluentAssertions;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-20 - Unit tests for MMR re-ranking algorithm (Phase 3.2)
public class MmrReRankerTests
{
    [Fact]
    public void Rerank_EmptyInput_ReturnsEmpty()
    {
        var result = MmrReRanker.Rerank(new List<SearchResult>(), new float[4], topK: 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Rerank_TopKLargerThanInput_ReturnsAllResults()
    {
        var candidates = MakeCandidates(3);
        var result = MmrReRanker.Rerank(candidates, MakeEmbedding(1f), topK: 10);
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Rerank_TopK1_ReturnsHighestScoringResult()
    {
        // Arrange — 3 candidates, highest score = 0.9 at index 0
        var candidates = new List<SearchResult>
        {
            MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.9),
            MakeCandidate(embedding: new float[] { 0f, 1f }, score: 0.5),
            MakeCandidate(embedding: new float[] { 0.5f, 0.5f }, score: 0.3),
        };
        var queryEmbedding = new float[] { 1f, 0f }; // Most similar to first

        var result = MmrReRanker.Rerank(candidates, queryEmbedding, topK: 1, lambda: 1.0);

        result.Should().HaveCount(1);
        result[0].Should().BeSameAs(candidates[0]);
    }

    [Fact]
    public void Rerank_Lambda1_PureRelevance_ReturnsByScore()
    {
        // Arrange — lambda=1.0 means no diversity penalty; result should be sorted by query similarity
        var candidates = new List<SearchResult>
        {
            MakeCandidate(embedding: new float[] { 0.1f, 0.9f }, score: 0.3),
            MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.9),
            MakeCandidate(embedding: new float[] { 0.6f, 0.4f }, score: 0.6),
        };
        var queryEmbedding = new float[] { 1f, 0f }; // Fully aligned with candidates[1]

        var result = MmrReRanker.Rerank(candidates, queryEmbedding, topK: 3, lambda: 1.0);

        // lambda=1.0: cosine similarity to query = 1.0 for [1,0], 0.6/sqrt(0.52) for [0.6,0.4], ~0 for [0.1,0.9]
        result[0].Should().BeSameAs(candidates[1]); // Most similar to query
    }

    [Fact]
    public void Rerank_Lambda0_PureDiversity_AvoidsRedundant()
    {
        // Arrange — two very similar chunks (A, B) and one different (C)
        // With lambda=0, MMR purely minimizes similarity to selected, so after picking first, C should beat B
        var chunkA = MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.9);
        var chunkB = MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.85); // identical direction to A
        var chunkC = MakeCandidate(embedding: new float[] { 0f, 1f }, score: 0.5);  // orthogonal to A

        var candidates = new List<SearchResult> { chunkA, chunkB, chunkC };
        var queryEmbedding = new float[] { 1f, 0f };

        // lambda=0: after selecting one, next pick minimises similarity to already selected
        var result = MmrReRanker.Rerank(candidates, queryEmbedding, topK: 2, lambda: 0.0);

        result.Should().HaveCount(2);
        // Second result must be chunkC (most different from chunkA)
        result[1].Should().BeSameAs(chunkC);
    }

    [Fact]
    public void Rerank_NormalMmr_DiversifiesRedundantResults()
    {
        // Arrange — query=[1,1] (diagonal), chunkA=[1,0], chunkB=[1,0] (same as A), chunkC=[0,1] (orthogonal to A)
        // After selecting A, MMR score for:
        //   chunkB: 0.5*cos([1,0],[1,1]) - 0.5*cos([1,0],[1,0]) = 0.5*0.707 - 0.5*1.0 = -0.146
        //   chunkC: 0.5*cos([0,1],[1,1]) - 0.5*cos([0,1],[1,0]) = 0.5*0.707 - 0.5*0.0 = +0.354
        // So chunkC must be selected second
        var chunkA = MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.9);
        var chunkB = MakeCandidate(embedding: new float[] { 1f, 0f }, score: 0.8); // identical direction to A
        var chunkC = MakeCandidate(embedding: new float[] { 0f, 1f }, score: 0.7); // orthogonal to A

        var candidates = new List<SearchResult> { chunkA, chunkB, chunkC };
        // Argha - 2026-02-20 - Diagonal query ensures chunkC clearly beats chunkB after A is selected (Phase 3.2)
        var queryEmbedding = new float[] { 1f, 1f }; // 45° — equally aligned with both dimensions

        var result = MmrReRanker.Rerank(candidates, queryEmbedding, topK: 2, lambda: 0.5);

        result.Should().HaveCount(2);
        result[0].Should().BeSameAs(chunkA); // Best cosine sim to diagonal query among all
        result[1].Should().BeSameAs(chunkC); // C preferred over B (much less redundant with A)
    }

    [Fact]
    public void Rerank_NullEmbedding_FallsBackToScore()
    {
        // Arrange — candidates without embeddings (fallback to Score for similarity proxy)
        var candidates = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "a.txt", Content = "A", Score = 0.9, ChunkIndex = 0, Embedding = null },
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "b.txt", Content = "B", Score = 0.5, ChunkIndex = 0, Embedding = null },
        };

        // Should not throw; should return results ordered by score
        var result = MmrReRanker.Rerank(candidates, new float[4], topK: 2, lambda: 1.0);

        result.Should().HaveCount(2);
        result[0].Score.Should().Be(0.9);
    }

    // --- helpers ---

    private static SearchResult MakeCandidate(float[] embedding, double score = 0.8)
        => new()
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = "test.txt",
            Content = "content",
            Score = score,
            ChunkIndex = 0,
            Embedding = embedding
        };

    private static List<SearchResult> MakeCandidates(int count)
        => Enumerable.Range(0, count).Select(i => new SearchResult
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = $"doc{i}.txt",
            Content = $"Content {i}",
            Score = 0.9 - i * 0.1,
            ChunkIndex = i,
            Embedding = new float[] { 1f, 0f, 0f, 0f }
        }).ToList();

    private static float[] MakeEmbedding(float value)
        => new[] { value, 0f, 0f, 0f };
}

using System.Runtime.CompilerServices;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RagApi.Application.Services;

/// <summary>
/// Main RAG service that orchestrates document retrieval and AI responses
/// </summary>
public class RagService
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChatService _chatService;
    private readonly ILogger<RagService> _logger;
    // Argha - 2026-02-20 - SearchOptions for hybrid search config 
    private readonly SearchOptions _searchOptions;

    private const string SystemPromptTemplate = @"You are a helpful AI assistant that answers questions based on the provided context.
Use ONLY the information from the context below to answer the question. If the context doesn't contain enough information to answer the question, say so clearly.

When answering:
1. Be accurate and cite specific information from the context
2. If you quote from the context, use quotation marks
3. Keep your answer concise but complete
4. If multiple documents contain relevant information, synthesize them

Context from documents:
{0}

Remember: Only use information from the context above. Do not make up information.";

    // Argha - 2026-02-20 - Added IOptions<SearchOptions> for hybrid search 
    public RagService(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IChatService chatService,
        ILogger<RagService> logger,
        IOptions<SearchOptions> searchOptions)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _chatService = chatService;
        _logger = logger;
        _searchOptions = searchOptions.Value;
    }

    /// <summary>
    /// Process a chat query using RAG (Retrieval-Augmented Generation)
    /// </summary>
    // Argha - 2026-02-19 - Added filterByTags parameter for metadata tag filtering 
    // Argha - 2026-02-20 - Added useHybridSearch parameter for hybrid search 
    // Argha - 2026-02-20 - Added useReRanking parameter for MMR re-ranking 
    public async Task<ChatResponse> ChatAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        bool? useHybridSearch = null,
        bool? useReRanking = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing RAG query: {Query}", query);

        // Generate embedding for the query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        _logger.LogDebug("Generated query embedding with {Dimensions} dimensions", queryEmbedding.Length);

        // Argha - 2026-02-20 - Use hybrid search if enabled by request or config 
        // Argha - 2026-02-20 - Use MMR re-ranking if enabled by request or config 
        var searchResults = await RetrieveChunksAsync(
            query, queryEmbedding, topK, filterByDocumentId, filterByTags,
            useHybridSearch ?? _searchOptions.UseHybridSearch,
            useReRanking ?? _searchOptions.UseReRanking,
            cancellationToken);

        _logger.LogInformation("Found {Count} relevant chunks", searchResults.Count);

        if (searchResults.Count == 0)
        {
            return new ChatResponse
            {
                Answer = "I couldn't find any relevant information in the uploaded documents to answer your question. Please make sure you've uploaded documents that contain information related to your query.",
                Sources = new List<SourceCitation>(),
                Model = _chatService.ModelName
            };
        }

        // Build context from search results
        var context = BuildContext(searchResults);

        // Build the system prompt with context
        var systemPrompt = string.Format(SystemPromptTemplate, context);

        // Prepare conversation history
        var messages = conversationHistory?.ToList() ?? new List<ChatMessage>();
        messages.Add(new ChatMessage { Role = "user", Content = query });

        // Generate response using the chat service
        var response = await _chatService.GenerateResponseAsync(systemPrompt, messages, cancellationToken);

        // Build and return the response with citations
        return new ChatResponse
        {
            Answer = response,
            Sources = searchResults.Select(r => new SourceCitation
            {
                DocumentId = r.DocumentId,
                FileName = r.FileName,
                RelevantText = TruncateText(r.Content, 200),
                RelevanceScore = r.Score,
                ChunkIndex = r.ChunkIndex
            }).ToList(),
            Model = _chatService.ModelName
        };
    }

    /// <summary>
    /// Process a chat query using RAG and stream the response as a sequence of events
    /// </summary>
    // Argha - 2026-02-19 - Streaming variant of ChatAsync for SSE endpoint 
    // Argha - 2026-02-19 - Added filterByTags parameter for metadata tag filtering 
    // Argha - 2026-02-20 - Added useHybridSearch parameter for hybrid search 
    // Argha - 2026-02-20 - Added useReRanking parameter for MMR re-ranking 
    public async IAsyncEnumerable<StreamEvent> ChatStreamAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        bool? useHybridSearch = null,
        bool? useReRanking = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing streaming RAG query: {Query}", query);

        // Generate embedding for the query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Argha - 2026-02-20 - Use hybrid search if enabled by request or config 
        // Argha - 2026-02-20 - Use MMR re-ranking if enabled by request or config 
        var searchResults = await RetrieveChunksAsync(
            query, queryEmbedding, topK, filterByDocumentId, filterByTags,
            useHybridSearch ?? _searchOptions.UseHybridSearch,
            useReRanking ?? _searchOptions.UseReRanking,
            cancellationToken);
        _logger.LogInformation("Found {Count} relevant chunks for streaming", searchResults.Count);

        // Build source citations from search results
        var sources = searchResults.Select(r => new SourceCitation
        {
            DocumentId = r.DocumentId,
            FileName = r.FileName,
            RelevantText = TruncateText(r.Content, 200),
            RelevanceScore = r.Score,
            ChunkIndex = r.ChunkIndex
        }).ToList();

        // Argha - 2026-02-19 - Yield sources first so the client can render citations while tokens stream in
        yield return new StreamEvent { Type = "sources", Sources = sources, Model = _chatService.ModelName };

        if (searchResults.Count == 0)
        {
            // Yield the fallback message as a single token event, skip LLM call
            yield return new StreamEvent
            {
                Type = "token",
                Content = "I couldn't find any relevant information in the uploaded documents to answer your question. Please make sure you've uploaded documents that contain information related to your query."
            };
            yield break;
        }

        // Build context and system prompt
        var context = BuildContext(searchResults);
        var systemPrompt = string.Format(SystemPromptTemplate, context);

        // Prepare conversation messages
        var messages = conversationHistory?.ToList() ?? new List<ChatMessage>();
        messages.Add(new ChatMessage { Role = "user", Content = query });

        // Stream each LLM token as a token event
        await foreach (var token in _chatService.GenerateResponseStreamAsync(systemPrompt, messages, cancellationToken))
        {
            yield return new StreamEvent { Type = "token", Content = token };
        }
    }

    /// <summary>
    /// Perform semantic search without generating a chat response
    /// </summary>
    // Argha - 2026-02-19 - Added filterByTags parameter for metadata tag filtering 
    // Argha - 2026-02-20 - Added useHybridSearch parameter for hybrid search 
    // Argha - 2026-02-20 - Added useReRanking parameter for MMR re-ranking 
    public async Task<List<SearchResult>> SearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        List<string>? filterByTags = null,
        bool? useHybridSearch = null,
        bool? useReRanking = null,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        return await RetrieveChunksAsync(
            query, queryEmbedding, topK, filterByDocumentId, filterByTags,
            useHybridSearch ?? _searchOptions.UseHybridSearch,
            useReRanking ?? _searchOptions.UseReRanking,
            cancellationToken);
    }

    // Argha - 2026-02-20 - Unified retrieval entry point: semantic-only or hybrid + optional MMR re-ranking (Phase 3.1/3.2)
    private async Task<List<SearchResult>> RetrieveChunksAsync(
        string query,
        float[] queryEmbedding,
        int topK,
        Guid? filterByDocumentId,
        List<string>? filterByTags,
        bool useHybrid,
        bool useReRanking,
        CancellationToken cancellationToken)
    {
        // Argha - 2026-02-20 - Expand candidate count when hybrid (good fusion needs more candidates from each list)
        //                     or when re-ranking (MMR needs more candidates to diversify from). (Phase 3.1/3.2)
        var candidateCount = (useHybrid || useReRanking)
            ? topK * _searchOptions.CandidateMultiplier
            : topK;

        List<SearchResult> candidates;

        if (!useHybrid)
        {
            // Argha - 2026-02-20 - Use SearchWithEmbeddingsAsync when re-ranking is requested 
            candidates = useReRanking
                ? await _vectorStore.SearchWithEmbeddingsAsync(
                    queryEmbedding, candidateCount, filterByDocumentId, filterByTags, cancellationToken)
                : await _vectorStore.SearchAsync(
                    queryEmbedding, topK, filterByDocumentId, filterByTags, cancellationToken);
        }
        else
        {
            // Argha - 2026-02-20 - Hybrid: run semantic + keyword in parallel with expanded candidates 
            var semanticTask = useReRanking
                ? _vectorStore.SearchWithEmbeddingsAsync(
                    queryEmbedding, candidateCount, filterByDocumentId, filterByTags, cancellationToken)
                : _vectorStore.SearchAsync(
                    queryEmbedding, candidateCount, filterByDocumentId, filterByTags, cancellationToken);

            var keywordTask = _vectorStore.KeywordSearchAsync(
                query, candidateCount, filterByDocumentId, filterByTags, cancellationToken);

            await Task.WhenAll(semanticTask, keywordTask);

            _logger.LogDebug(
                "Hybrid search: {Semantic} semantic + {Keyword} keyword candidates",
                semanticTask.Result.Count, keywordTask.Result.Count);

            candidates = FuseWithRrf(semanticTask.Result, keywordTask.Result, candidateCount);
        }

        // Argha - 2026-02-20 - Apply MMR re-ranking if enabled 
        if (useReRanking && candidates.Count > 0)
        {
            _logger.LogDebug("Applying MMR re-ranking with lambda={Lambda}", _searchOptions.MmrLambda);
            return MmrReRanker.Rerank(candidates, queryEmbedding, topK, _searchOptions.MmrLambda);
        }

        return candidates;
    }

    // Argha - 2026-02-20 - Reciprocal Rank Fusion: score = Σ 1/(60 + rank) across both lists 
    private static List<SearchResult> FuseWithRrf(
        List<SearchResult> semanticResults,
        List<SearchResult> keywordResults,
        int topK,
        int k = 60)
    {
        var scores = new Dictionary<Guid, (double Score, SearchResult Result)>();

        void AccumulateRrf(List<SearchResult> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var rrfScore = 1.0 / (k + i + 1);
                if (scores.TryGetValue(r.ChunkId, out var existing))
                    scores[r.ChunkId] = (existing.Score + rrfScore, existing.Result);
                else
                    scores[r.ChunkId] = (rrfScore, r);
            }
        }

        AccumulateRrf(semanticResults);
        AccumulateRrf(keywordResults);

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x =>
            {
                // Argha - 2026-02-20 - Replace placeholder score with the fused RRF score 
                x.Result.Score = x.Score;
                return x.Result;
            })
            .ToList();
    }

    private static string BuildContext(List<SearchResult> results)
    {
        var contextBuilder = new System.Text.StringBuilder();
        
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            contextBuilder.AppendLine($"[Source {i + 1}: {result.FileName}]");
            contextBuilder.AppendLine(result.Content);
            contextBuilder.AppendLine();
        }

        return contextBuilder.ToString();
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength - 3) + "...";
    }
}

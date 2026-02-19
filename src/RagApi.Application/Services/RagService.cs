using System.Runtime.CompilerServices;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;

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

    public RagService(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IChatService chatService,
        ILogger<RagService> logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Process a chat query using RAG (Retrieval-Augmented Generation)
    /// </summary>
    public async Task<ChatResponse> ChatAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        int topK = 5,
        Guid? filterByDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing RAG query: {Query}", query);

        // Generate embedding for the query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        _logger.LogDebug("Generated query embedding with {Dimensions} dimensions", queryEmbedding.Length);

        // Search for relevant document chunks
        var searchResults = await _vectorStore.SearchAsync(
            queryEmbedding, 
            topK, 
            filterByDocumentId, 
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
    // Argha - 2026-02-19 - Streaming variant of ChatAsync for SSE endpoint (Phase 2.1)
    public async IAsyncEnumerable<StreamEvent> ChatStreamAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        int topK = 5,
        Guid? filterByDocumentId = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing streaming RAG query: {Query}", query);

        // Generate embedding for the query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search for relevant document chunks
        var searchResults = await _vectorStore.SearchAsync(queryEmbedding, topK, filterByDocumentId, cancellationToken);
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
    public async Task<List<SearchResult>> SearchAsync(
        string query,
        int topK = 5,
        Guid? filterByDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        return await _vectorStore.SearchAsync(queryEmbedding, topK, filterByDocumentId, cancellationToken);
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

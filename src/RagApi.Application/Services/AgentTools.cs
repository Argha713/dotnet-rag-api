using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

// Argha - 2026-03-22 - #65 - Result from a single tool call in the agent loop
public class ToolResult
{
    public string Text { get; set; } = string.Empty;
    public List<SourceCitation> Sources { get; set; } = new();
}

// Argha - 2026-03-22 - #65 - Thin wrappers over existing infrastructure for the 4 agent tools
public class AgentTools
{
    private readonly RagService _ragService;
    private readonly IChatService _chatService;
    private readonly ILogger<AgentTools> _logger;

    public AgentTools(
        RagService ragService,
        IChatService chatService,
        ILogger<AgentTools> logger)
    {
        _ragService = ragService;
        _chatService = chatService;
        _logger = logger;
    }

    // Argha - 2026-03-22 - #65 - Dispatch tool call by name; unknown tools return a safe error text
    public async Task<ToolResult> ExecuteAsync(string toolName, string toolInput, CancellationToken ct)
    {
        _logger.LogDebug("Agent executing tool '{Tool}' with input: {Input}", toolName, toolInput);

        return toolName switch
        {
            "search_documents"     => await SearchDocumentsAsync(toolInput, ct),
            "get_document_summary" => await GetDocumentSummaryAsync(toolInput, ct),
            "compare_chunks"       => await CompareChunksAsync(toolInput, ct),
            _                      => new ToolResult { Text = $"Unknown tool '{toolName}'. Available tools: search_documents, get_document_summary, compare_chunks, answer_directly." }
        };
    }

    // Argha - 2026-03-22 - #65 - Tool 1: search the document store for relevant chunks
    private async Task<ToolResult> SearchDocumentsAsync(string query, CancellationToken ct)
    {
        var results = await _ragService.SearchAsync(query, topK: 5, cancellationToken: ct);

        if (results.Count == 0)
            return new ToolResult { Text = "No relevant documents found for this query." };

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} relevant chunk(s):");
        sb.AppendLine();

        foreach (var r in results)
        {
            sb.AppendLine($"[{r.FileName}] (score: {r.Score:F3})");
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }

        return new ToolResult
        {
            Text = sb.ToString(),
            Sources = results.Select(r => new SourceCitation
            {
                DocumentId = r.DocumentId,
                FileName = r.FileName,
                RelevantText = r.Content.Length > 200 ? r.Content[..200] + "..." : r.Content,
                RelevanceScore = r.Score,
                ChunkIndex = r.ChunkIndex
            }).ToList()
        };
    }

    // Argha - 2026-03-22 - #65 - Tool 2: retrieve chunks for a document and summarise them
    private async Task<ToolResult> GetDocumentSummaryAsync(string documentNameOrQuery, CancellationToken ct)
    {
        var results = await _ragService.SearchAsync(documentNameOrQuery, topK: 8, cancellationToken: ct);

        if (results.Count == 0)
            return new ToolResult { Text = $"No document found matching '{documentNameOrQuery}'." };

        var chunks = string.Join("\n\n", results.Select(r => r.Content));
        var systemPrompt = "You are a document summariser. Summarise the following document excerpts in 3-5 sentences. Focus on the main topics, key facts, and overall purpose of the document.";
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = $"Summarise these document excerpts:\n\n{chunks}" }
        };

        var summary = await _chatService.GenerateResponseAsync(systemPrompt, messages, ct);

        return new ToolResult
        {
            Text = $"Summary for '{results[0].FileName}':\n{summary}",
            Sources = results.Take(3).Select(r => new SourceCitation
            {
                DocumentId = r.DocumentId,
                FileName = r.FileName,
                RelevantText = r.Content.Length > 200 ? r.Content[..200] + "..." : r.Content,
                RelevanceScore = r.Score,
                ChunkIndex = r.ChunkIndex
            }).ToList()
        };
    }

    // Argha - 2026-03-22 - #65 - Tool 3: compare two text segments and identify agreements/conflicts
    // Input is JSON: {"text1": "...", "text2": "..."}
    private async Task<ToolResult> CompareChunksAsync(string jsonInput, CancellationToken ct)
    {
        string text1, text2;
        try
        {
            using var doc = JsonDocument.Parse(jsonInput);
            text1 = doc.RootElement.GetProperty("text1").GetString() ?? string.Empty;
            text2 = doc.RootElement.GetProperty("text2").GetString() ?? string.Empty;
        }
        catch
        {
            // Argha - 2026-03-22 - #65 - Fallback: treat the entire input as text1 if JSON is malformed
            text1 = jsonInput;
            text2 = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(text1))
            return new ToolResult { Text = "compare_chunks requires non-empty text1 in the JSON input." };

        var systemPrompt = "You are an analyst. Compare the two text segments below. Identify: (1) agreements or similarities, (2) disagreements or contradictions, (3) information present in one but not the other. Be specific and concise.";
        var userContent = string.IsNullOrWhiteSpace(text2)
            ? $"Analyse this text:\n\nText 1:\n{text1}"
            : $"Compare these two texts:\n\nText 1:\n{text1}\n\nText 2:\n{text2}";

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = userContent }
        };

        var comparison = await _chatService.GenerateResponseAsync(systemPrompt, messages, ct);
        return new ToolResult { Text = comparison };
    }
}

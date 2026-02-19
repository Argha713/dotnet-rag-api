using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using RagApi.Api.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Api.Controllers;

/// <summary>
/// Controller for RAG chat operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ChatController : ControllerBase
{
    private readonly RagService _ragService;

    public ChatController(RagService ragService)
    {
        _ragService = ragService;
    }

    /// <summary>
    /// Ask a question and get an AI-powered answer based on uploaded documents
    /// </summary>
    /// <param name="request">The chat request with query and options</param>
    /// <returns>AI-generated answer with source citations</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Convert conversation history if provided
        List<ChatMessage>? history = null;
        if (request.ConversationHistory?.Count > 0)
        {
            history = request.ConversationHistory
                .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
                .ToList();
        }

        var response = await _ragService.ChatAsync(
            request.Query,
            history,
            request.TopK,
            request.DocumentId,
            cancellationToken);

        var dto = new ChatResponseDto
        {
            Answer = response.Answer,
            Model = response.Model,
            Sources = response.Sources.Select(s => new SourceDto
            {
                DocumentId = s.DocumentId,
                FileName = s.FileName,
                RelevantText = s.RelevantText,
                RelevanceScore = s.RelevanceScore
            }).ToList()
        };

        return Ok(dto);
    }

    /// <summary>
    /// Stream an AI-powered answer using Server-Sent Events (SSE)
    /// </summary>
    /// <remarks>
    /// Returns a text/event-stream. Each event is a JSON object with a "type" field:
    /// - "sources": emitted first with source citations and model name
    /// - "token": one per LLM output token
    /// - "done": signals end of stream
    /// </remarks>
    // Argha - 2026-02-19 - SSE streaming endpoint for Phase 2.1
    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Argha - 2026-02-19 - SSE requires these three headers for correct client behaviour
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Convert conversation history if provided
        List<ChatMessage>? history = null;
        if (request.ConversationHistory?.Count > 0)
        {
            history = request.ConversationHistory
                .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
                .ToList();
        }

        // Argha - 2026-02-19 - camelCase + null-ignored for clean SSE output
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        try
        {
            await foreach (var streamEvent in _ragService.ChatStreamAsync(
                request.Query, history, request.TopK, request.DocumentId, cancellationToken))
            {
                var json = JsonSerializer.Serialize(streamEvent, jsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: {\"type\":\"done\"}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Argha - 2026-02-19 - Client disconnected mid-stream, exit silently
        }
    }

    /// <summary>
    /// Perform semantic search without generating an AI response
    /// </summary>
    /// <param name="request">The search request</param>
    /// <returns>List of relevant document chunks</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(List<SearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var results = await _ragService.SearchAsync(
            request.Query,
            request.TopK,
            request.DocumentId,
            cancellationToken);

        var dtos = results.Select(r => new SearchResultDto
        {
            ChunkId = r.ChunkId,
            DocumentId = r.DocumentId,
            FileName = r.FileName,
            Content = r.Content,
            Score = r.Score,
            ChunkIndex = r.ChunkIndex
        }).ToList();

        return Ok(dtos);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
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
    private readonly ConversationService _conversationService;

    // Argha - 2026-02-20 - FV validators for complex rules not covered by data annotations (Phase 4.2)
    private readonly IValidator<ChatRequest> _chatValidator;
    private readonly IValidator<SearchRequest> _searchValidator;

    // Argha - 2026-02-19 - Injected ConversationService for server-side session support (Phase 2.2)
    // Argha - 2026-02-20 - Added FV validators for ChatRequest and SearchRequest (Phase 4.2)
    public ChatController(
        RagService ragService,
        ConversationService conversationService,
        IValidator<ChatRequest> chatValidator,
        IValidator<SearchRequest> searchValidator)
    {
        _ragService = ragService;
        _conversationService = conversationService;
        _chatValidator = chatValidator;
        _searchValidator = searchValidator;
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

        // Argha - 2026-02-20 - Run FluentValidation for complex rules (Tags list, ConversationMessage.Role) (Phase 4.2)
        var fvResult = await _chatValidator.ValidateAsync(request, cancellationToken);
        if (!fvResult.IsValid)
        {
            foreach (var error in fvResult.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        // Argha - 2026-02-19 - SessionId takes precedence over ConversationHistory (Phase 2.2)
        List<ChatMessage>? history;
        if (request.SessionId.HasValue)
        {
            history = await _conversationService.GetHistoryAsync(request.SessionId.Value, cancellationToken);
            if (history == null)
                return NotFound($"Session {request.SessionId.Value} not found.");
        }
        else
        {
            history = request.ConversationHistory?.Count > 0
                ? request.ConversationHistory.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }).ToList()
                : null;
        }

        var response = await _ragService.ChatAsync(
            request.Query,
            history,
            request.TopK,
            request.DocumentId,
            // Argha - 2026-02-19 - Pass tags filter for metadata-based retrieval (Phase 2.3)
            request.Tags,
            // Argha - 2026-02-20 - Pass per-request hybrid search override (Phase 3.1)
            request.UseHybridSearch,
            // Argha - 2026-02-20 - Pass per-request MMR re-ranking override (Phase 3.2)
            request.UseReRanking,
            cancellationToken);

        // Argha - 2026-02-19 - Persist turn to session if SessionId was provided (Phase 2.2)
        if (request.SessionId.HasValue)
        {
            await _conversationService.AppendMessagesAsync(
                request.SessionId.Value, request.Query, response.Answer, cancellationToken);
        }

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

        // Argha - 2026-02-20 - Run FluentValidation before SSE headers are set so a 400 can still be returned (Phase 4.2)
        var fvResult = await _chatValidator.ValidateAsync(request, cancellationToken);
        if (!fvResult.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Argha - 2026-02-19 - Resolve session history before setting SSE headers so we can still return 404 (Phase 2.2)
        List<ChatMessage>? history;
        if (request.SessionId.HasValue)
        {
            history = await _conversationService.GetHistoryAsync(request.SessionId.Value, cancellationToken);
            if (history == null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        else
        {
            history = request.ConversationHistory?.Count > 0
                ? request.ConversationHistory.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }).ToList()
                : null;
        }

        // Argha - 2026-02-19 - SSE requires these three headers for correct client behaviour
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Argha - 2026-02-19 - camelCase + null-ignored for clean SSE output
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Argha - 2026-02-19 - Accumulate tokens to persist full answer to session after streaming (Phase 2.2)
        var answerBuilder = request.SessionId.HasValue ? new StringBuilder() : null;

        try
        {
            await foreach (var streamEvent in _ragService.ChatStreamAsync(
                // Argha - 2026-02-19 - Pass tags filter through to streaming pipeline (Phase 2.3)
                // Argha - 2026-02-20 - Pass per-request hybrid search override (Phase 3.1)
                // Argha - 2026-02-20 - Pass per-request MMR re-ranking override (Phase 3.2)
                request.Query, history, request.TopK, request.DocumentId, request.Tags, request.UseHybridSearch, request.UseReRanking, cancellationToken))
            {
                if (streamEvent.Type == "token")
                    answerBuilder?.Append(streamEvent.Content);

                var json = JsonSerializer.Serialize(streamEvent, jsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            await Response.WriteAsync("data: {\"type\":\"done\"}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            // Argha - 2026-02-19 - Persist user query + assembled answer to session (Phase 2.2)
            if (request.SessionId.HasValue && answerBuilder != null)
            {
                await _conversationService.AppendMessagesAsync(
                    request.SessionId.Value, request.Query, answerBuilder.ToString(), cancellationToken);
            }
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

        // Argha - 2026-02-20 - Run FluentValidation for Tags list constraints (Phase 4.2)
        var fvResult = await _searchValidator.ValidateAsync(request, cancellationToken);
        if (!fvResult.IsValid)
        {
            foreach (var error in fvResult.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return ValidationProblem(ModelState);
        }

        var results = await _ragService.SearchAsync(
            request.Query,
            request.TopK,
            request.DocumentId,
            // Argha - 2026-02-19 - Pass tags filter for tag-scoped semantic search (Phase 2.3)
            request.Tags,
            // Argha - 2026-02-20 - Pass per-request hybrid search override (Phase 3.1)
            request.UseHybridSearch,
            // Argha - 2026-02-20 - Pass per-request MMR re-ranking override (Phase 3.2)
            request.UseReRanking,
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

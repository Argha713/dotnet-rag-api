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
    private readonly ILogger<ChatController> _logger;

    public ChatController(RagService ragService, ILogger<ChatController> logger)
    {
        _ragService = ragService;
        _logger = logger;
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process chat request");
            return BadRequest(new { error = ex.Message });
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process search request");
            return BadRequest(new { error = ex.Message });
        }
    }
}

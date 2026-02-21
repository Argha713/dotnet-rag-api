using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagApi.Api.Models;
using RagApi.Application.Services;

namespace RagApi.Api.Controllers;

// Argha - 2026-02-19 - CRUD controller for server-side conversation sessions 

/// <summary>
/// Controller for managing server-side conversation sessions
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConversationsController : ControllerBase
{
    private readonly ConversationService _conversationService;

    // Argha - 2026-02-19 - Deserializing ChatMessage JSON (PascalCase) into SessionMessageDto
    private static readonly JsonSerializerOptions JsonOpts = new();

    public ConversationsController(ConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    /// <summary>
    /// Create a new conversation session
    /// </summary>
    /// <returns>The new session ID and creation timestamp</returns>
    [HttpPost]
    [ProducesResponseType(typeof(CreateSessionResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        var session = await _conversationService.CreateSessionAsync(cancellationToken);

        var response = new CreateSessionResponse
        {
            SessionId = session.Id,
            CreatedAt = session.CreatedAt
        };

        return CreatedAtAction(nameof(GetSession), new { id = session.Id }, response);
    }

    /// <summary>
    /// Get a conversation session with its full message history
    /// </summary>
    /// <param name="id">Session ID</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await _conversationService.GetSessionAsync(id, cancellationToken);
        if (session == null) return NotFound();

        var messages = JsonSerializer.Deserialize<List<SessionMessageDto>>(session.MessagesJson, JsonOpts)
                       ?? new List<SessionMessageDto>();

        var dto = new SessionDto
        {
            SessionId = session.Id,
            CreatedAt = session.CreatedAt,
            LastMessageAt = session.LastMessageAt,
            Title = session.Title,
            Messages = messages
        };

        return Ok(dto);
    }

    /// <summary>
    /// Delete a conversation session and all its messages
    /// </summary>
    /// <param name="id">Session ID</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _conversationService.DeleteSessionAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

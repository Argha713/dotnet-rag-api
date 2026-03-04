using Microsoft.AspNetCore.Mvc;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Api.Controllers;

// Argha - 2026-03-04 - #17 - Workspace CRUD: create, get, delete workspaces for multi-tenant isolation
[ApiController]
[Route("api/workspaces")]
[Produces("application/json")]
public class WorkspacesController : ControllerBase
{
    private readonly WorkspaceService _workspaceService;
    private readonly IWorkspaceContext _workspaceContext;

    public WorkspacesController(WorkspaceService workspaceService, IWorkspaceContext workspaceContext)
    {
        _workspaceService = workspaceService;
        _workspaceContext = workspaceContext;
    }

    /// <summary>
    /// Create a new workspace. Returns the plaintext API key — shown once only.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkspaceCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateWorkspace(
        [FromBody] CreateWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            return BadRequest(new { error = "Name is required." });

        var (workspace, plainTextKey) = await _workspaceService.CreateWorkspaceAsync(request.Name, cancellationToken);

        return CreatedAtAction(
            nameof(GetWorkspace),
            new { id = workspace.Id },
            new WorkspaceCreatedDto(
                workspace.Id,
                workspace.Name,
                workspace.CreatedAt,
                workspace.CollectionName,
                plainTextKey));
    }

    /// <summary>
    /// Get workspace metadata by ID (API key is not returned).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WorkspaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkspace(Guid id, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceService.GetWorkspaceAsync(id, cancellationToken);
        if (workspace == null)
            return NotFound();

        return Ok(new WorkspaceDto(workspace.Id, workspace.Name, workspace.CreatedAt, workspace.CollectionName));
    }

    /// <summary>
    /// Delete a workspace and all its data (documents, conversations, Qdrant collection).
    /// Only the owning workspace key may delete its workspace; the global key may delete any workspace.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWorkspace(Guid id, CancellationToken cancellationToken)
    {
        var currentWorkspaceId = _workspaceContext.Current.Id;
        // Argha - 2026-03-04 - #17 - Default workspace = global key; can delete any. Others can only delete themselves.
        var isGlobalKey = currentWorkspaceId == Workspace.DefaultWorkspaceId;
        if (!isGlobalKey && currentWorkspaceId != id)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "Access denied. You can only delete your own workspace." });
        }

        try
        {
            await _workspaceService.DeleteWorkspaceAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

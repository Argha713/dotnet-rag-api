using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Infrastructure;

namespace RagApi.Api.Controllers;

/// <summary>
/// Controller for system health and statistics
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class SystemController : ControllerBase
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChatService _chatService;
    private readonly DocumentService _documentService;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<SystemController> _logger;
    // Argha - 2026-03-04 - #17 - Provides the workspace's collection name for stats scoping
    private readonly IWorkspaceContext _workspaceContext;

    public SystemController(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IChatService chatService,
        DocumentService documentService,
        IOptions<AiConfiguration> aiConfig,
        ILogger<SystemController> logger,
        IWorkspaceContext workspaceContext)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _chatService = chatService;
        _documentService = documentService;
        _aiConfig = aiConfig.Value;
        _logger = logger; // Argha - 2026-02-15 - Kept for intentional graceful degradation in GetStats
        _workspaceContext = workspaceContext;
    }

    // Health check endpoint moved to MapHealthChecks("/api/system/health") in Program.cs

    /// <summary>
    /// Get system statistics
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(SystemStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        try
        {
            // Argha - 2026-03-04 - #17 - Stats scoped to the current workspace's collection
            var vectorStats = await _vectorStore.GetStatsAsync(_workspaceContext.Current.CollectionName, cancellationToken);
            var documents = await _documentService.GetAllDocumentsAsync();

            return Ok(new SystemStatsDto
            {
                TotalDocuments = documents.Count,
                TotalVectors = vectorStats.TotalVectors,
                AiProvider = _aiConfig.Provider,
                EmbeddingModel = _embeddingService.ModelName,
                ChatModel = _chatService.ModelName,
                EmbeddingDimension = _embeddingService.EmbeddingDimension
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system stats");
            return Ok(new SystemStatsDto
            {
                AiProvider = _aiConfig.Provider,
                EmbeddingModel = _embeddingService.ModelName,
                ChatModel = _chatService.ModelName,
                EmbeddingDimension = _embeddingService.EmbeddingDimension
            });
        }
    }
}

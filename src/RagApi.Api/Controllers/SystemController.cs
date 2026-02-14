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

    public SystemController(
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        IChatService chatService,
        DocumentService documentService,
        IOptions<AiConfiguration> aiConfig,
        ILogger<SystemController> logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _chatService = chatService;
        _documentService = documentService;
        _aiConfig = aiConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new 
        { 
            status = "healthy", 
            timestamp = DateTime.UtcNow,
            provider = _aiConfig.Provider
        });
    }

    /// <summary>
    /// Get system statistics
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(SystemStatsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        try
        {
            var vectorStats = await _vectorStore.GetStatsAsync(cancellationToken);
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

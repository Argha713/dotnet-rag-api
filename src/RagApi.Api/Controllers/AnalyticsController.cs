using Microsoft.AspNetCore.Mvc;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;

namespace RagApi.Api.Controllers;

// Argha - 2026-03-22 - #85 - Analytics endpoint: aggregated query metrics for a workspace
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly IQueryLogRepository _queryLogRepository;
    private readonly IWorkspaceContext _workspaceContext;

    public AnalyticsController(
        IQueryLogRepository queryLogRepository,
        IWorkspaceContext workspaceContext)
    {
        _queryLogRepository = queryLogRepository;
        _workspaceContext = workspaceContext;
    }

    /// <summary>
    /// Get aggregated analytics for the current workspace.
    /// </summary>
    /// <param name="days">Number of days to include (1–30, default 7)</param>
    [HttpGet]
    [ProducesResponseType(typeof(AnalyticsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAnalytics(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-22 - #85 - Clamp days to 1–30 to protect Neon free tier from large scans
        days = Math.Clamp(days, 1, 30);

        var workspaceId = _workspaceContext.Current.Id;
        var dto = await _queryLogRepository.GetAnalyticsAsync(workspaceId, days, cancellationToken);

        return Ok(dto);
    }
}

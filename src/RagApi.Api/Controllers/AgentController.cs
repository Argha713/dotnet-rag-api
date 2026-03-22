using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Api.Controllers;

// Argha - 2026-03-22 - #65 - Agentic RAG endpoints: multi-step ReAct loop over documents
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AgentController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly AgentConfiguration _config;
    // Argha - 2026-03-22 - #85 - Query audit log for agent queries
    private readonly IQueryLogService _queryLogService;
    // Argha - 2026-03-22 - #85 - Workspace context for log scoping
    private readonly IWorkspaceContext _workspaceContext;

    // Argha - 2026-03-22 - #85 - Added IQueryLogService and IWorkspaceContext for Phase 13 analytics
    public AgentController(
        AgentService agentService,
        IOptions<AgentConfiguration> config,
        IQueryLogService queryLogService,
        IWorkspaceContext workspaceContext)
    {
        _agentService = agentService;
        _config = config.Value;
        _queryLogService = queryLogService;
        _workspaceContext = workspaceContext;
    }

    /// <summary>
    /// Run the agentic ReAct loop and return a complete response with reasoning trace.
    /// Returns 503 when Agent:Enabled is false.
    /// </summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Query([FromBody] AgentQueryRequest request, CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Agentic RAG is currently disabled.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var sw = Stopwatch.StartNew();
        var result = await _agentService.RunAsync(request.Query, cancellationToken);

        // Argha - 2026-03-22 - #85 - Log agent query; SourceCount from result.Sources
        var agentDto = new AgentResponseDto
        {
            Answer = result.Answer,
            Model = result.Model,
            IterationsUsed = result.IterationsUsed,
            MaxIterationsReached = result.MaxIterationsReached,
            Sources = result.Sources.Select(s => new SourceDto
            {
                DocumentId = s.DocumentId,
                FileName = s.FileName,
                RelevantText = s.RelevantText,
                RelevanceScore = s.RelevanceScore
            }).ToList(),
            Trace = new AgentTraceDto
            {
                Steps = result.Trace.Steps.Select(s => new AgentStepDto
                {
                    StepNumber = s.StepNumber,
                    StepType = s.StepType,
                    Reasoning = s.Reasoning,
                    ToolName = s.ToolName,
                    ToolInput = s.ToolInput,
                    ToolOutput = s.ToolOutput
                }).ToList()
            }
        };

        await _queryLogService.LogAsync(new QueryLogEntry(
            _workspaceContext.Current.Id,
            QueryType.Agent,
            request.Query,
            result.Model,
            0,
            sw.ElapsedMilliseconds,
            result.Sources.Count,
            true), cancellationToken);

        return Ok(agentDto);
    }

    /// <summary>
    /// Stream the agentic ReAct loop as Server-Sent Events.
    /// Each event is a JSON AgentStreamEvent. Final event type is "done".
    /// Returns 503 when Agent:Enabled is false.
    /// </summary>
    // Argha - 2026-03-22 - #85 - SSE endpoint not logged: streaming accumulation is out of scope for Phase 13
    // Argha - 2026-03-22 - #65 - SSE streaming: client sees each reasoning step as it happens
    [HttpPost("stream")]
    public async Task StreamQuery([FromBody] AgentQueryRequest request, CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // Argha - 2026-03-22 - #65 - camelCase + null-ignored for clean SSE output
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        int iterationsUsed = 0;
        bool maxReached = false;
        var allSources = new List<SourceDto>();
        string finalAnswer = string.Empty;

        try
        {
            await foreach (var step in _agentService.RunStreamAsync(request.Query, cancellationToken))
            {
                var stepDto = new AgentStepDto
                {
                    StepNumber = step.StepNumber,
                    StepType = step.StepType,
                    Reasoning = step.Reasoning,
                    ToolName = step.ToolName,
                    ToolInput = step.ToolInput,
                    ToolOutput = step.ToolOutput
                };

                if (step.StepType == "answer")
                    finalAnswer = step.ToolOutput ?? string.Empty;

                if (step.StepType == "max_iterations_reached")
                {
                    maxReached = true;
                    iterationsUsed = step.StepNumber;
                }

                if (step.StepType == "tool_call")
                    iterationsUsed = (step.StepNumber + 2) / 3;

                var evt = new AgentStreamEvent { Type = "step", Step = stepDto };
                var json = JsonSerializer.Serialize(evt, jsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }

            var doneEvt = new AgentStreamEvent
            {
                Type = "done",
                Answer = finalAnswer,
                Sources = allSources,
                IterationsUsed = iterationsUsed,
                MaxIterationsReached = maxReached
            };
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(doneEvt, jsonOptions)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Argha - 2026-03-22 - #65 - Client disconnected mid-stream; exit silently
        }
    }
}

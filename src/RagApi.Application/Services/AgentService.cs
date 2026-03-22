using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

// Argha - 2026-03-22 - #65 - Parsed output of a single LLM step in the ReAct loop
public record AgentAction(string Thought, string Action, string ActionInput);

// Argha - 2026-03-22 - #65 - Orchestrates the ReAct (Reason→Act→Observe) loop for agentic RAG
public class AgentService
{
    private readonly AgentTools _tools;
    private readonly IChatService _chatService;
    private readonly ILogger<AgentService> _logger;
    private readonly AgentConfiguration _config;

    private const string ToolListDescription = @"Available tools:
- search_documents: Search the document store for relevant information. Input: a plain-text search query.
- get_document_summary: Get a high-level summary of what a document contains. Input: a document filename or descriptive query.
- compare_chunks: Compare two pieces of text and identify agreements, contradictions, or relationships. Input: JSON object with ""text1"" and ""text2"" string fields.
- answer_directly: Provide the final answer when you have sufficient information. Input: the complete answer text.";

    // Argha - 2026-03-22 - #65 - System prompt instructs the LLM to output structured JSON at every step
    private const string SystemPromptTemplate = @"You are an AI research agent that answers questions by searching documents step by step.

At EVERY step you MUST output ONLY a single valid JSON object — no other text, no markdown, no explanation outside the JSON:
{{
  ""thought"": ""Your reasoning about what to do next"",
  ""action"": ""one of: search_documents | get_document_summary | compare_chunks | answer_directly"",
  ""action_input"": ""the input for the chosen action""
}}

{0}

Rules:
1. Output ONLY the JSON object — nothing before or after it.
2. Use tools to gather information before answering. Do not answer from memory.
3. Call answer_directly only when you have enough information to answer the user's question fully.
4. For compare_chunks, action_input must be a JSON string: {{""text1"": ""..."", ""text2"": ""...""}}.
5. Never fabricate document content.

User question: {1}";

    public AgentService(
        AgentTools tools,
        IChatService chatService,
        ILogger<AgentService> logger,
        IOptions<AgentConfiguration> config)
    {
        _tools = tools;
        _chatService = chatService;
        _logger = logger;
        _config = config.Value;
    }

    // Argha - 2026-03-22 - #65 - Run the full ReAct loop and return a complete AgentResponse
    public async Task<AgentResponse> RunAsync(string query, CancellationToken ct = default)
    {
        var trace = new AgentTrace();
        var allSources = new List<SourceCitation>();
        var messages = new List<ChatMessage>();
        var systemPrompt = string.Format(SystemPromptTemplate, ToolListDescription, query);

        // Argha - 2026-03-22 - #65 - Seed with the user question so conversation history builds correctly
        messages.Add(new ChatMessage { Role = "user", Content = query });

        int iteration = 0;
        bool maxReached = false;
        string finalAnswer = string.Empty;

        while (iteration < _config.MaxIterations)
        {
            iteration++;
            _logger.LogDebug("Agent iteration {Iteration}/{Max}", iteration, _config.MaxIterations);

            var llmResponse = await _chatService.GenerateResponseAsync(systemPrompt, messages, ct);
            var parsed = ParseAgentAction(llmResponse);

            // Argha - 2026-03-22 - #65 - Record reasoning step
            trace.Steps.Add(new AgentStep
            {
                StepNumber = trace.Steps.Count + 1,
                StepType = "reasoning",
                Reasoning = parsed.Thought
            });

            if (parsed.Action == "answer_directly")
            {
                finalAnswer = parsed.ActionInput;
                trace.Steps.Add(new AgentStep
                {
                    StepNumber = trace.Steps.Count + 1,
                    StepType = "answer",
                    ToolOutput = finalAnswer
                });
                break;
            }

            // Argha - 2026-03-22 - #65 - Record tool call
            trace.Steps.Add(new AgentStep
            {
                StepNumber = trace.Steps.Count + 1,
                StepType = "tool_call",
                ToolName = parsed.Action,
                ToolInput = parsed.ActionInput
            });

            var toolResult = await _tools.ExecuteAsync(parsed.Action, parsed.ActionInput, ct);
            allSources.AddRange(toolResult.Sources);

            // Argha - 2026-03-22 - #65 - Record tool result
            trace.Steps.Add(new AgentStep
            {
                StepNumber = trace.Steps.Count + 1,
                StepType = "tool_result",
                ToolName = parsed.Action,
                ToolOutput = toolResult.Text
            });

            // Argha - 2026-03-22 - #65 - Append assistant turn + observation so the LLM sees its history
            messages.Add(new ChatMessage { Role = "assistant", Content = llmResponse });
            messages.Add(new ChatMessage { Role = "user", Content = $"Tool result for {parsed.Action}:\n{toolResult.Text}\n\nContinue your reasoning." });
        }

        if (string.IsNullOrEmpty(finalAnswer))
        {
            maxReached = true;
            _logger.LogWarning("Agent reached max iterations ({Max}) without calling answer_directly; forcing final answer", _config.MaxIterations);
            var forceMessages = messages.ToList();
            forceMessages.Add(new ChatMessage
            {
                Role = "user",
                Content = "Based on all the information gathered so far, provide your best answer to the original question. Output plain text, not JSON."
            });
            finalAnswer = await _chatService.GenerateResponseAsync(
                "Provide a clear, direct answer based on the research gathered. Plain text only.",
                forceMessages, ct);
        }

        return new AgentResponse
        {
            Answer = finalAnswer,
            Sources = allSources.DistinctBy(s => new { s.DocumentId, s.ChunkIndex }).ToList(),
            Model = _chatService.ModelName,
            Trace = trace,
            IterationsUsed = iteration,
            MaxIterationsReached = maxReached
        };
    }

    // Argha - 2026-03-22 - #65 - Streaming variant: yields AgentStep events as the loop progresses
    public async IAsyncEnumerable<AgentStep> RunStreamAsync(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();
        var systemPrompt = string.Format(SystemPromptTemplate, ToolListDescription, query);
        messages.Add(new ChatMessage { Role = "user", Content = query });

        for (int iteration = 1; iteration <= _config.MaxIterations; iteration++)
        {
            _logger.LogDebug("Agent stream iteration {Iteration}/{Max}", iteration, _config.MaxIterations);

            var llmResponse = await _chatService.GenerateResponseAsync(systemPrompt, messages, ct);
            var parsed = ParseAgentAction(llmResponse);

            var reasoningStep = new AgentStep
            {
                StepNumber = (iteration - 1) * 3 + 1,
                StepType = "reasoning",
                Reasoning = parsed.Thought
            };
            yield return reasoningStep;

            if (parsed.Action == "answer_directly")
            {
                yield return new AgentStep
                {
                    StepNumber = reasoningStep.StepNumber + 1,
                    StepType = "answer",
                    ToolOutput = parsed.ActionInput
                };
                yield break;
            }

            var callStep = new AgentStep
            {
                StepNumber = reasoningStep.StepNumber + 1,
                StepType = "tool_call",
                ToolName = parsed.Action,
                ToolInput = parsed.ActionInput
            };
            yield return callStep;

            var toolResult = await _tools.ExecuteAsync(parsed.Action, parsed.ActionInput, ct);

            var resultStep = new AgentStep
            {
                StepNumber = callStep.StepNumber + 1,
                StepType = "tool_result",
                ToolName = parsed.Action,
                ToolOutput = toolResult.Text
            };
            yield return resultStep;

            messages.Add(new ChatMessage { Role = "assistant", Content = llmResponse });
            messages.Add(new ChatMessage { Role = "user", Content = $"Tool result for {parsed.Action}:\n{toolResult.Text}\n\nContinue your reasoning." });
        }

        // Argha - 2026-03-22 - #65 - Max iterations reached without answer_directly — yield a forced-answer step
        _logger.LogWarning("Agent stream reached max iterations; emitting max-iterations-reached step");
        yield return new AgentStep
        {
            StepNumber = _config.MaxIterations * 3 + 1,
            StepType = "max_iterations_reached",
            Reasoning = "Maximum iterations reached. Returning best available answer."
        };
    }

    // Argha - 2026-03-22 - #65 - Extract thought/action/action_input from the LLM JSON response
    // Falls back to a safe default if JSON cannot be parsed
    public static AgentAction ParseAgentAction(string llmResponse)
    {
        var trimmed = llmResponse.Trim();

        // Argha - 2026-03-22 - #65 - Strip markdown code fences if the model wrapped the JSON
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var thought = root.TryGetProperty("thought", out var t) ? t.GetString() ?? "" : "";
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "answer_directly" : "answer_directly";
            var actionInput = root.TryGetProperty("action_input", out var ai)
                ? (ai.ValueKind == JsonValueKind.String ? ai.GetString() ?? "" : ai.GetRawText())
                : "";

            return new AgentAction(thought, action, actionInput);
        }
        catch
        {
            // Argha - 2026-03-22 - #65 - Unparseable response: treat the whole text as the final answer
            return new AgentAction(
                Thought: "Could not parse structured response from model.",
                Action: "answer_directly",
                ActionInput: llmResponse.Trim());
        }
    }
}

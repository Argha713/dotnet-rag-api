using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-03-22 - #65 - Unit tests for AgentService ReAct loop and ParseAgentAction
public class AgentServiceTests
{
    // ───── ParseAgentAction tests ─────────────────────────────────────────────

    [Fact]
    public void ParseAgentAction_ValidJson_ExtractsCorrectly()
    {
        var json = """{"thought":"I need to search","action":"search_documents","action_input":"pricing strategy"}""";

        var result = AgentService.ParseAgentAction(json);

        result.Thought.Should().Be("I need to search");
        result.Action.Should().Be("search_documents");
        result.ActionInput.Should().Be("pricing strategy");
    }

    [Fact]
    public void ParseAgentAction_InvalidJson_ReturnsFallbackToAnswerDirectly()
    {
        var raw = "This is not JSON at all.";

        var result = AgentService.ParseAgentAction(raw);

        result.Action.Should().Be("answer_directly");
        result.ActionInput.Should().Be(raw);
    }

    [Fact]
    public void ParseAgentAction_JsonWrappedInCodeFences_StripsAndParses()
    {
        var fenced = "```json\n{\"thought\":\"ok\",\"action\":\"answer_directly\",\"action_input\":\"Done.\"}\n```";

        var result = AgentService.ParseAgentAction(fenced);

        result.Action.Should().Be("answer_directly");
        result.ActionInput.Should().Be("Done.");
    }

    [Fact]
    public void ParseAgentAction_MissingActionField_DefaultsToAnswerDirectly()
    {
        var json = """{"thought":"hmm"}""";

        var result = AgentService.ParseAgentAction(json);

        result.Action.Should().Be("answer_directly");
    }

    [Fact]
    public void ParseAgentAction_ActionInputIsJsonObject_ReturnsRawJson()
    {
        var json = """{"thought":"compare","action":"compare_chunks","action_input":{"text1":"a","text2":"b"}}""";

        var result = AgentService.ParseAgentAction(json);

        result.Action.Should().Be("compare_chunks");
        result.ActionInput.Should().Contain("text1");
    }

    // ───── RunAsync loop tests ────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AnswerDirectlyOnFirstIteration_ReturnsAfterOneIteration()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 5);
        chatMock.SetupSequence(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"I know this.","action":"answer_directly","action_input":"The answer is 42."}""");

        var result = await sut.RunAsync("What is the answer?");

        result.Answer.Should().Be("The answer is 42.");
        result.IterationsUsed.Should().Be(1);
        result.MaxIterationsReached.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_MaxIterationsReached_SetsFlag()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 2);
        // Argha - 2026-03-22 - #65 - Always return a tool call so the loop never gets answer_directly
        chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"searching","action":"search_documents","action_input":"test"}""");

        var result = await sut.RunAsync("A complex question?");

        result.MaxIterationsReached.Should().BeTrue();
        result.IterationsUsed.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_ToolCallThenAnswer_TraceContainsAllStepTypes()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 5);
        chatMock.SetupSequence(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            // Argha - 2026-03-22 - #65 - First call: search; second call: forced answer after tool result
            .ReturnsAsync("""{"thought":"need to search","action":"search_documents","action_input":"test query"}""")
            .ReturnsAsync("""{"thought":"found enough","action":"answer_directly","action_input":"Here is the answer."}""");

        var result = await sut.RunAsync("Tell me about X");

        var stepTypes = result.Trace.Steps.Select(s => s.StepType).ToList();
        stepTypes.Should().Contain("reasoning");
        stepTypes.Should().Contain("tool_call");
        stepTypes.Should().Contain("tool_result");
        stepTypes.Should().Contain("answer");
    }

    [Fact]
    public async Task RunAsync_SearchDocumentsToolCall_PopulatesSources()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 5, searchResultCount: 2);
        chatMock.SetupSequence(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"searching","action":"search_documents","action_input":"revenue"}""")
            .ReturnsAsync("""{"thought":"done","action":"answer_directly","action_input":"Revenue was up."}""");

        var result = await sut.RunAsync("What was the revenue?");

        result.Sources.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_Answer_ModelNameComesFromChatService()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 5);
        chatMock.SetupSequence(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"ok","action":"answer_directly","action_input":"Done."}""");

        var result = await sut.RunAsync("Q?");

        result.Model.Should().Be("test-model");
    }

    [Fact]
    public async Task RunAsync_MaxIterationsOne_OnlyOneIterationAllowed()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 1);
        chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"still searching","action":"search_documents","action_input":"x"}""");

        var result = await sut.RunAsync("question");

        result.IterationsUsed.Should().Be(1);
        result.MaxIterationsReached.Should().BeTrue();
    }

    // ───── RunStreamAsync tests ───────────────────────────────────────────────

    [Fact]
    public async Task RunStreamAsync_AnswerDirectly_YieldsReasoningThenAnswerStep()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 5);
        chatMock.SetupSequence(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"ready","action":"answer_directly","action_input":"The answer."}""");

        var steps = new List<AgentStep>();
        await foreach (var step in sut.RunStreamAsync("Q?"))
            steps.Add(step);

        steps.Should().HaveCount(2);
        steps[0].StepType.Should().Be("reasoning");
        steps[1].StepType.Should().Be("answer");
    }

    [Fact]
    public async Task RunStreamAsync_MaxIterationsReached_YieldsMaxIterationsReachedStep()
    {
        var (sut, chatMock) = CreateSut(maxIterations: 1);
        chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"searching","action":"search_documents","action_input":"x"}""");

        var steps = new List<AgentStep>();
        await foreach (var step in sut.RunStreamAsync("Q?"))
            steps.Add(step);

        steps.Last().StepType.Should().Be("max_iterations_reached");
    }

    // ───── Helper ─────────────────────────────────────────────────────────────

    private (AgentService sut, Mock<IChatService> chatMock) CreateSut(int maxIterations = 5, int searchResultCount = 0)
    {
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.ModelName).Returns("test-model");
        // Argha - 2026-03-22 - #65 - Default GenerateResponseAsync (overridden per-test with SetupSequence)
        chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"thought":"ok","action":"answer_directly","action_input":"fallback answer"}""");

        var embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        var vectorStoreMock = new Mock<IVectorStore>();
        var searchResults = Enumerable.Range(0, searchResultCount).Select(i => new SearchResult
        {
            ChunkId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            FileName = $"doc{i}.txt",
            Content = $"content {i}",
            Score = 0.9 - i * 0.1
        }).ToList();

        vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        var workspaceContext = new Mock<IWorkspaceContext>();
        workspaceContext.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = "documents"
        });

        var ragService = new RagService(
            vectorStoreMock.Object,
            embeddingMock.Object,
            chatMock.Object,
            Mock.Of<ILogger<RagService>>(),
            Options.Create(new SearchOptions()),
            workspaceContext.Object);

        var tools = new AgentTools(ragService, chatMock.Object, Mock.Of<ILogger<AgentTools>>());

        var config = Options.Create(new AgentConfiguration { MaxIterations = maxIterations, Enabled = true });

        var sut = new AgentService(tools, chatMock.Object, Mock.Of<ILogger<AgentService>>(), config);

        return (sut, chatMock);
    }
}

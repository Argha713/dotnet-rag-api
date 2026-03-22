using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-03-22 - #65 - Unit tests for AgentController endpoints
public class AgentControllerTests
{
    // ───── POST /api/agent/query ──────────────────────────────────────────────

    [Fact]
    public async Task Query_ValidRequest_Returns200WithAgentResponse()
    {
        var (controller, _) = CreateController(enabled: true, answerJson:
            """{"thought":"ready","action":"answer_directly","action_input":"The answer is here."}""");

        var result = await controller.Query(new AgentQueryRequest { Query = "What is RAG?" }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<AgentResponseDto>().Subject;
        dto.Answer.Should().Be("The answer is here.");
        dto.IterationsUsed.Should().Be(1);
        dto.MaxIterationsReached.Should().BeFalse();
    }

    [Fact]
    public async Task Query_WhenDisabled_Returns503()
    {
        var (controller, _) = CreateController(enabled: false);

        var result = await controller.Query(new AgentQueryRequest { Query = "test" }, CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Query_InvalidModelState_Returns400()
    {
        var (controller, _) = CreateController(enabled: true);
        controller.ModelState.AddModelError("Query", "Required");

        var result = await controller.Query(new AgentQueryRequest { Query = "" }, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Query_TraceIsPopulatedInResponse()
    {
        var (controller, _) = CreateController(enabled: true, answerJson:
            """{"thought":"I can answer this directly.","action":"answer_directly","action_input":"42"}""");

        var result = await controller.Query(new AgentQueryRequest { Query = "Meaning of life?" }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<AgentResponseDto>().Subject;
        dto.Trace.Steps.Should().NotBeEmpty();
        dto.Trace.Steps.Should().Contain(s => s.StepType == "reasoning");
        dto.Trace.Steps.Should().Contain(s => s.StepType == "answer");
    }

    [Fact]
    public async Task Query_MaxIterationsReached_SetsFlag()
    {
        // Argha - 2026-03-22 - #65 - Never returns answer_directly → hits max iterations
        var (controller, _) = CreateController(
            enabled: true,
            maxIterations: 1,
            answerJson: """{"thought":"still searching","action":"search_documents","action_input":"x"}""");

        var result = await controller.Query(new AgentQueryRequest { Query = "Hard question" }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<AgentResponseDto>().Subject;
        dto.MaxIterationsReached.Should().BeTrue();
    }

    // ───── POST /api/agent/stream ─────────────────────────────────────────────

    [Fact]
    public async Task StreamQuery_WhenDisabled_Sets503StatusCode()
    {
        var (controller, _) = CreateController(enabled: false);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new System.IO.MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.StreamQuery(new AgentQueryRequest { Query = "test" }, CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task StreamQuery_InvalidModelState_Sets400StatusCode()
    {
        var (controller, _) = CreateController(enabled: true);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new System.IO.MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.ModelState.AddModelError("Query", "Required");

        await controller.StreamQuery(new AgentQueryRequest { Query = "" }, CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task StreamQuery_ValidRequest_WritesEventStreamContentType()
    {
        var (controller, _) = CreateController(enabled: true, answerJson:
            """{"thought":"ready","action":"answer_directly","action_input":"Done."}""");

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new System.IO.MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.StreamQuery(new AgentQueryRequest { Query = "Q?" }, CancellationToken.None);

        httpContext.Response.ContentType.Should().Be("text/event-stream");
    }

    // ───── AgentConfiguration defaults test ──────────────────────────────────

    [Fact]
    public void AgentConfiguration_Defaults_AreCorrect()
    {
        var config = new AgentConfiguration();

        config.MaxIterations.Should().Be(5);
        config.Enabled.Should().BeTrue();
        AgentConfiguration.SectionName.Should().Be("Agent");
    }

    // ───── Helper ─────────────────────────────────────────────────────────────

    private (AgentController controller, Mock<IChatService> chatMock) CreateController(
        bool enabled,
        int maxIterations = 5,
        string answerJson = """{"thought":"ok","action":"answer_directly","action_input":"answer"}""")
    {
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(c => c.ModelName).Returns("test-model");
        chatMock.Setup(c => c.GenerateResponseAsync(
                It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answerJson);

        var embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        var vectorStoreMock = new Mock<IVectorStore>();
        vectorStoreMock.Setup(v => v.SearchAsync(
                It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

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
        var agentConfig = Options.Create(new AgentConfiguration { MaxIterations = maxIterations, Enabled = true });
        var agentService = new AgentService(tools, chatMock.Object, Mock.Of<ILogger<AgentService>>(), agentConfig);

        var controllerConfig = Options.Create(new AgentConfiguration { MaxIterations = maxIterations, Enabled = enabled });
        var controller = new AgentController(agentService, controllerConfig);

        return (controller, chatMock);
    }
}

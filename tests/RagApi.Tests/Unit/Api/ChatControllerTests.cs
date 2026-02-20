using System.Runtime.CompilerServices;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
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

// Argha - 2026-02-15 - Unit tests for ChatController endpoints (Phase 1.5)
// Argha - 2026-02-19 - Extended with session-based chat tests (Phase 2.2)
public class ChatControllerTests
{
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IEmbeddingService> _embeddingMock;
    private readonly Mock<IChatService> _chatMock;
    private readonly Mock<IConversationRepository> _conversationRepoMock;
    private readonly ChatController _sut;

    private static readonly float[] TestEmbedding = new float[768];

    public ChatControllerTests()
    {
        _vectorStoreMock = new Mock<IVectorStore>();
        _embeddingMock = new Mock<IEmbeddingService>();
        _chatMock = new Mock<IChatService>();
        _conversationRepoMock = new Mock<IConversationRepository>();

        _embeddingMock.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestEmbedding);
        _chatMock.Setup(c => c.ModelName).Returns("llama3.2");

        // Argha - 2026-02-15 - Use real RagService with mocked dependencies since it's a concrete class
        // Argha - 2026-02-20 - Pass default SearchOptions (UseHybridSearch=false) (Phase 3.1)
        var ragService = new RagService(
            _vectorStoreMock.Object,
            _embeddingMock.Object,
            _chatMock.Object,
            Mock.Of<ILogger<RagService>>(),
            Options.Create(new SearchOptions()));

        // Argha - 2026-02-19 - Real ConversationService with mocked repository (Phase 2.2)
        var conversationService = new ConversationService(
            _conversationRepoMock.Object,
            Mock.Of<ILogger<ConversationService>>());

        // Argha - 2026-02-20 - Pass always-valid FV mocks so existing tests are unaffected (Phase 4.2)
        var chatValidatorMock = new Mock<IValidator<ChatRequest>>();
        chatValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var searchValidatorMock = new Mock<IValidator<SearchRequest>>();
        searchValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _sut = new ChatController(ragService, conversationService, chatValidatorMock.Object, searchValidatorMock.Object);
    }

    [Fact]
    public async Task Chat_ValidRequest_Returns200WithResponse()
    {
        // Arrange
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "relevant content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test answer");

        var request = new ChatRequest { Query = "What is AI?" };

        // Act
        var result = await _sut.Chat(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<ChatResponseDto>().Subject;
        dto.Answer.Should().Be("Test answer");
        dto.Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task Chat_MapsSourcesCorrectly()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = docId, FileName = "source.pdf", Content = "text", Score = 0.85, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer");

        // Act
        var result = await _sut.Chat(new ChatRequest { Query = "test" }, CancellationToken.None);

        // Assert
        var dto = ((OkObjectResult)result).Value as ChatResponseDto;
        dto!.Sources.Should().HaveCount(1);
        dto.Sources[0].DocumentId.Should().Be(docId);
        dto.Sources[0].FileName.Should().Be("source.pdf");
        dto.Sources[0].RelevanceScore.Should().Be(0.85);
    }

    [Fact]
    public async Task Search_ValidRequest_Returns200WithResults()
    {
        // Arrange
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        var request = new SearchRequest { Query = "test query" };

        // Act
        var result = await _sut.Search(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<List<SearchResultDto>>().Subject;
        dtos.Should().HaveCount(1);
        dtos[0].Content.Should().Be("content");
    }

    [Fact]
    public async Task Search_FiltersByDocumentId()
    {
        // Arrange
        var docId = Guid.NewGuid();
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, docId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        var request = new SearchRequest { Query = "query", DocumentId = docId };

        // Act
        await _sut.Search(request, CancellationToken.None);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 5, docId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Argha - 2026-02-19 - Session-based chat tests (Phase 2.2)

    [Fact]
    public async Task Chat_WithSessionId_LoadsHistoryFromSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var existingSession = new ConversationSession
        {
            Id = sessionId,
            MessagesJson = System.Text.Json.JsonSerializer.Serialize(new List<ChatMessage>
            {
                new() { Role = "user", Content = "Previous question" },
                new() { Role = "assistant", Content = "Previous answer" }
            })
        };
        _conversationRepoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSession);
        _conversationRepoMock.Setup(r => r.AppendMessagesAsync(
                sessionId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("New answer");

        // Act
        var result = await _sut.Chat(new ChatRequest { Query = "New question", SessionId = sessionId }, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _chatMock.Verify(c => c.GenerateResponseAsync(
            It.IsAny<string>(),
            It.Is<List<ChatMessage>>(msgs => msgs.Any(m => m.Content == "Previous question")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Chat_WithSessionId_AppendsMessagesToSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _conversationRepoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationSession { Id = sessionId, MessagesJson = "[]" });
        _conversationRepoMock.Setup(r => r.AppendMessagesAsync(
                sessionId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("The answer");

        // Act
        await _sut.Chat(new ChatRequest { Query = "My question", SessionId = sessionId }, CancellationToken.None);

        // Assert
        _conversationRepoMock.Verify(r => r.AppendMessagesAsync(
            sessionId, "My question", "The answer", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Chat_WithInvalidSessionId_Returns404()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _conversationRepoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        // Act
        var result = await _sut.Chat(new ChatRequest { Query = "question", SessionId = sessionId }, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // Argha - 2026-02-19 - Streaming endpoint tests (Phase 2.1)

    [Fact]
    public async Task StreamChat_ValidRequest_WritesSseEvents()
    {
        // Arrange
        var searchResults = new List<SearchResult>
        {
            new() { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), FileName = "doc.txt", Content = "content", Score = 0.9, ChunkIndex = 0 }
        };
        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);
        _chatMock.Setup(c => c.GenerateResponseStreamAsync(
                It.IsAny<string>(), It.IsAny<List<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .Returns(CreateTestTokenStream("Hello", " world"));

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        await _sut.StreamChat(new ChatRequest { Query = "What is AI?" }, CancellationToken.None);

        // Assert
        httpContext.Response.ContentType.Should().Be("text/event-stream");
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"type\":\"sources\"");
        body.Should().Contain("\"type\":\"token\"");
        body.Should().Contain("\"type\":\"done\"");
    }

    [Fact]
    public async Task StreamChat_InvalidRequest_Returns400()
    {
        // Arrange
        // Argha - 2026-02-19 - ControllerContext must be set before adding ModelState errors,
        //   because ModelState is backed by ControllerContext.ModelState and replacing the context
        //   discards previously added errors
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        _sut.ControllerContext = new ControllerContext { HttpContext = httpContext };
        _sut.ModelState.AddModelError("Query", "The Query field is required.");

        // Act
        await _sut.StreamChat(new ChatRequest { Query = string.Empty }, CancellationToken.None);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static async IAsyncEnumerable<string> CreateTestTokenStream(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        params string[] tokens)
    {
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return token;
            await Task.CompletedTask;
        }
    }

    // Argha - 2026-02-19 - Tag filtering test (Phase 2.3)
    [Fact]
    public async Task Search_FiltersByTags_PassesTagsToVectorStore()
    {
        // Arrange
        var tags = new List<string> { "finance" };
        _vectorStoreMock.Setup(v => v.SearchAsync(TestEmbedding, 5, null, tags, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SearchResult>());

        var request = new SearchRequest { Query = "budget", Tags = tags };

        // Act
        await _sut.Search(request, CancellationToken.None);

        // Assert
        _vectorStoreMock.Verify(v => v.SearchAsync(TestEmbedding, 5, null, tags, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Argha - 2026-02-19 - Overload without explicit cancellation token for call-site convenience
    private static async IAsyncEnumerable<string> CreateTestTokenStream(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            yield return token;
            await Task.CompletedTask;
        }
    }
}

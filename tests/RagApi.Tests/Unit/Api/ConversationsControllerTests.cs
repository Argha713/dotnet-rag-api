using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-02-19 - Unit tests for ConversationsController (Phase 2.2)
public class ConversationsControllerTests
{
    private readonly Mock<IConversationRepository> _repoMock;
    private readonly ConversationsController _sut;

    public ConversationsControllerTests()
    {
        _repoMock = new Mock<IConversationRepository>();

        // Argha - 2026-02-19 - Real ConversationService with mocked repository
        var conversationService = new ConversationService(
            _repoMock.Object,
            Mock.Of<ILogger<ConversationService>>());

        _sut = new ConversationsController(conversationService);
    }

    [Fact]
    public async Task CreateSession_Returns201WithSessionId()
    {
        // Arrange
        var session = new ConversationSession { Id = Guid.NewGuid() };
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Act
        var result = await _sut.CreateSession(CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var response = createdResult.Value.Should().BeOfType<CreateSessionResponse>().Subject;
        response.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task GetSession_ExistingSession_Returns200WithHistory()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi" }
        };
        var session = new ConversationSession
        {
            Id = sessionId,
            Title = "Hello",
            MessagesJson = JsonSerializer.Serialize(messages)
        };
        _repoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Act
        var result = await _sut.GetSession(sessionId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<SessionDto>().Subject;
        dto.SessionId.Should().Be(sessionId);
        dto.Title.Should().Be("Hello");
        dto.Messages.Should().HaveCount(2);
        dto.Messages[0].Role.Should().Be("user");
        dto.Messages[0].Content.Should().Be("Hello");
    }

    [Fact]
    public async Task GetSession_NonExistentSession_Returns404()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        // Act
        var result = await _sut.GetSession(sessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteSession_ExistingSession_Returns204()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.DeleteAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteSession(sessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteSession_NonExistentSession_Returns404()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.DeleteAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteSession(sessionId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }
}

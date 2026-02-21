using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-19 - Unit tests for ConversationService 
public class ConversationServiceTests
{
    private readonly Mock<IConversationRepository> _repoMock;
    private readonly ConversationService _sut;

    public ConversationServiceTests()
    {
        _repoMock = new Mock<IConversationRepository>();
        _sut = new ConversationService(_repoMock.Object, Mock.Of<ILogger<ConversationService>>());
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsSessionWithNewId()
    {
        // Arrange
        var session = new ConversationSession();
        _repoMock.Setup(r => r.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Act
        var result = await _sut.CreateSessionAsync();

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.MessagesJson.Should().Be("[]");
        _repoMock.Verify(r => r.CreateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetHistoryAsync_ExistingSession_ReturnsDeserializedMessages()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi there" }
        };
        var session = new ConversationSession
        {
            Id = sessionId,
            MessagesJson = JsonSerializer.Serialize(messages)
        };
        _repoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        // Act
        var result = await _sut.GetHistoryAsync(sessionId);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result[0].Role.Should().Be("user");
        result[0].Content.Should().Be("Hello");
        result[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task GetHistoryAsync_NonExistentSession_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        // Act
        var result = await _sut.GetHistoryAsync(sessionId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AppendMessagesAsync_CallsRepositoryWithCorrectArgs()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.AppendMessagesAsync(
                sessionId, "user query", "assistant answer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.AppendMessagesAsync(sessionId, "user query", "assistant answer");

        // Assert
        result.Should().BeTrue();
        _repoMock.Verify(r => r.AppendMessagesAsync(
            sessionId, "user query", "assistant answer", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_ExistingSession_ReturnsTrue()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.DeleteAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.DeleteSessionAsync(sessionId);

        // Assert
        result.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteSessionAsync_NonExistentSession_ReturnsFalse()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        _repoMock.Setup(r => r.DeleteAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await _sut.DeleteSessionAsync(sessionId);

        // Assert
        result.Should().BeFalse();
    }
}

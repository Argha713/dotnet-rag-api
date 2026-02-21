using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

// Argha - 2026-02-21 - Unit tests for ConversationExportService 
public class ConversationExportServiceTests
{
    private readonly Mock<IConversationRepository> _repoMock;
    private readonly ConversationExportService _sut;

    // Argha - 2026-02-21 - Fixed session for reuse across tests
    private static readonly Guid SessionId = Guid.NewGuid();

    private static readonly ConversationSession SessionWithMessages = new()
    {
        Id           = SessionId,
        Title        = "Test Conversation",
        CreatedAt    = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        LastMessageAt = new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
        MessagesJson = JsonSerializer.Serialize(new List<ChatMessage>
        {
            new() { Role = "user",      Content = "Hello",    Timestamp = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) },
            new() { Role = "assistant", Content = "Hi there", Timestamp = new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc) }
        })
    };

    public ConversationExportServiceTests()
    {
        _repoMock = new Mock<IConversationRepository>();

        var conversationService = new ConversationService(
            _repoMock.Object,
            Mock.Of<ILogger<ConversationService>>());

        _sut = new ConversationExportService(conversationService);
    }

    [Fact]
    public async Task ExportAsync_JsonFormat_ReturnsJsonWithCorrectFields()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var result = await _sut.ExportAsync(SessionId, "json", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("application/json");
        result.FileName.Should().Be($"conversation-{SessionId}.json");

        var json = Encoding.UTF8.GetString(result.Content);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("sessionId").GetGuid().Should().Be(SessionId);
        root.GetProperty("title").GetString().Should().Be("Test Conversation");
        root.GetProperty("messageCount").GetInt32().Should().Be(2);
        root.GetProperty("messages").GetArrayLength().Should().Be(2);
        root.GetProperty("exportedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExportAsync_MarkdownFormat_ReturnsMarkdownContent()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var result = await _sut.ExportAsync(SessionId, "markdown", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("text/markdown");
        result.FileName.Should().Be($"conversation-{SessionId}.md");

        var text = Encoding.UTF8.GetString(result.Content);
        text.Should().Contain("# Test Conversation");
        text.Should().Contain("### User");
        text.Should().Contain("### Assistant");
        text.Should().Contain("Hello");
        text.Should().Contain("Hi there");
    }

    [Fact]
    public async Task ExportAsync_TextFormat_ReturnsPlainTextContent()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var result = await _sut.ExportAsync(SessionId, "text", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("text/plain");
        result.FileName.Should().Be($"conversation-{SessionId}.txt");

        var text = Encoding.UTF8.GetString(result.Content);
        text.Should().Contain("Conversation: Test Conversation");
        text.Should().Contain("[User -");
        text.Should().Contain("[Assistant -");
        text.Should().Contain("Hello");
        text.Should().Contain("Hi there");
    }

    [Fact]
    public async Task ExportAsync_MdAlias_TreatedAsMarkdown()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var result = await _sut.ExportAsync(SessionId, "md", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("text/markdown");
        result.FileName.Should().EndWith(".md");
    }

    [Fact]
    public async Task ExportAsync_TxtAlias_TreatedAsText()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var result = await _sut.ExportAsync(SessionId, "txt", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("text/plain");
        result.FileName.Should().EndWith(".txt");
    }

    [Fact]
    public async Task ExportAsync_SessionNotFound_ReturnsNull()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        // Act
        var result = await _sut.ExportAsync(missingId, "json", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExportAsync_EmptyMessages_JsonStillValid()
    {
        // Arrange
        var emptySession = new ConversationSession
        {
            Id           = SessionId,
            Title        = "Empty",
            CreatedAt    = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow,
            MessagesJson = "[]"
        };
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptySession);

        // Act
        var result = await _sut.ExportAsync(SessionId, "json", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var json = Encoding.UTF8.GetString(result!.Content);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("messageCount").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("messages").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExportAsync_UnsupportedFormat_ThrowsArgumentException()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionWithMessages);

        // Act
        var act = () => _sut.ExportAsync(SessionId, "csv", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*csv*");
    }
}

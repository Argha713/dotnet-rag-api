using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-02-21 - Unit tests for the export endpoint
public class ConversationsControllerExportTests
{
    private readonly Mock<IConversationRepository> _repoMock;
    private readonly ConversationsController _sut;

    private static readonly Guid SessionId = Guid.NewGuid();

    private static readonly ConversationSession SampleSession = new()
    {
        Id           = SessionId,
        Title        = "Export Test",
        CreatedAt    = new DateTime(2026, 2, 21, 9, 0, 0, DateTimeKind.Utc),
        LastMessageAt = new DateTime(2026, 2, 21, 9, 5, 0, DateTimeKind.Utc),
        MessagesJson = JsonSerializer.Serialize(new List<ChatMessage>
        {
            new() { Role = "user",      Content = "What is RAG?", Timestamp = new DateTime(2026, 2, 21, 9, 0, 0, DateTimeKind.Utc) },
            new() { Role = "assistant", Content = "RAG is ...",   Timestamp = new DateTime(2026, 2, 21, 9, 1, 0, DateTimeKind.Utc) }
        })
    };

    public ConversationsControllerExportTests()
    {
        _repoMock = new Mock<IConversationRepository>();

        var conversationService = new ConversationService(
            _repoMock.Object,
            Mock.Of<ILogger<ConversationService>>());

        var exportService = new ConversationExportService(conversationService);

        _sut = new ConversationsController(conversationService, exportService);
    }

    [Fact]
    public async Task ExportSession_JsonFormat_Returns200WithJsonFile()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSession);

        // Act
        var result = await _sut.ExportSession(SessionId, "json", CancellationToken.None);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/json");
        fileResult.FileDownloadName.Should().Be($"conversation-{SessionId}.json");
        fileResult.FileContents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExportSession_MarkdownFormat_Returns200WithMarkdownFile()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSession);

        // Act
        var result = await _sut.ExportSession(SessionId, "markdown", CancellationToken.None);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("text/markdown");
        fileResult.FileDownloadName.Should().Be($"conversation-{SessionId}.md");

        var text = Encoding.UTF8.GetString(fileResult.FileContents);
        text.Should().Contain("# Export Test");
    }

    [Fact]
    public async Task ExportSession_TextFormat_Returns200WithTextFile()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSession);

        // Act
        var result = await _sut.ExportSession(SessionId, "text", CancellationToken.None);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("text/plain");
        fileResult.FileDownloadName.Should().Be($"conversation-{SessionId}.txt");
    }

    [Fact]
    public async Task ExportSession_OmittedFormat_DefaultsToJson()
    {
        // Arrange — call without format param (uses default "json")
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSession);

        // Act
        var result = await _sut.ExportSession(SessionId, cancellationToken: CancellationToken.None);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task ExportSession_SessionNotFound_Returns404()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationSession?)null);

        // Act
        var result = await _sut.ExportSession(missingId, "json", CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ExportSession_InvalidFormat_Returns400()
    {
        // Arrange
        _repoMock.Setup(r => r.GetAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleSession);

        // Act
        var result = await _sut.ExportSession(SessionId, "csv", CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeOfType<string>()
            .Which.Should().Contain("csv");
    }
}

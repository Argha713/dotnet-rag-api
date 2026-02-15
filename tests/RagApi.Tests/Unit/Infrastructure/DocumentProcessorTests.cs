using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Infrastructure.DocumentProcessing;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-02-15 - Unit tests for DocumentProcessor chunking, content type, and text extraction (Phase 1.5)
public class DocumentProcessorTests
{
    private readonly DocumentProcessor _sut;

    public DocumentProcessorTests()
    {
        var loggerMock = new Mock<ILogger<DocumentProcessor>>();
        _sut = new DocumentProcessor(loggerMock.Object);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("text/plain")]
    [InlineData("text/markdown")]
    public void IsSupported_KnownTypes_ReturnsTrue(string contentType)
    {
        _sut.IsSupported(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/json")]
    [InlineData("video/mp4")]
    public void IsSupported_UnknownTypes_ReturnsFalse(string contentType)
    {
        _sut.IsSupported(contentType).Should().BeFalse();
    }

    [Fact]
    public void IsSupported_CaseInsensitive()
    {
        _sut.IsSupported("TEXT/PLAIN").Should().BeTrue();
        _sut.IsSupported("Application/PDF").Should().BeTrue();
    }

    [Fact]
    public void SupportedContentTypes_ContainsAllFourTypes()
    {
        _sut.SupportedContentTypes.Should().HaveCount(4);
        _sut.SupportedContentTypes.Should().Contain("application/pdf");
        _sut.SupportedContentTypes.Should().Contain("text/plain");
        _sut.SupportedContentTypes.Should().Contain("text/markdown");
    }

    [Fact]
    public async Task ExtractTextAsync_PlainText_ReadsContent()
    {
        // Arrange
        var text = "Hello world\nThis is a test document.";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

        // Act
        var result = await _sut.ExtractTextAsync(stream, "text/plain");

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public async Task ExtractTextAsync_UnsupportedType_Throws()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var act = () => _sut.ExtractTextAsync(stream, "image/png");

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void ChunkText_SplitsWithinSizeLimit()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var paragraph1 = new string('A', 600);
        var paragraph2 = new string('B', 600);
        var text = $"{paragraph1}\n\n{paragraph2}";
        var options = new ChunkingOptions { ChunkSize = 800, ChunkOverlap = 100 };

        // Act
        var chunks = _sut.ChunkText(docId, text, options);

        // Assert
        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
        chunks.All(c => c.DocumentId == docId).Should().BeTrue();
    }

    [Fact]
    public void ChunkText_IncludesOverlap()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var paragraph1 = new string('A', 600);
        var paragraph2 = new string('B', 600);
        var text = $"{paragraph1}\n\n{paragraph2}";
        var options = new ChunkingOptions { ChunkSize = 800, ChunkOverlap = 200 };

        // Act
        var chunks = _sut.ChunkText(docId, text, options);

        // Assert
        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
        // Argha - 2026-02-15 - Second chunk should start before the end of the first due to overlap
        if (chunks.Count >= 2)
        {
            chunks[1].StartPosition.Should().BeLessThan(chunks[0].EndPosition);
        }
    }

    [Fact]
    public void ChunkText_WhitespaceOnly_ReturnsEmpty()
    {
        // Arrange
        var docId = Guid.NewGuid();

        // Act
        var chunks = _sut.ChunkText(docId, "   \n\n   \t  ");

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkText_AssignsSequentialChunkIndices()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var text = "Paragraph one.\n\nParagraph two.\n\nParagraph three.";

        // Act
        var chunks = _sut.ChunkText(docId, text);

        // Assert
        chunks.Should().NotBeEmpty();
        for (int i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkIndex.Should().Be(i);
        }
    }
}

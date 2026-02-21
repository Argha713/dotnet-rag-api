using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Application.Services;
using RagApi.Infrastructure.DocumentProcessing;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-02-20 - Unit tests for configurable chunking strategies 
public class ChunkingStrategyTests
{
    private readonly DocumentProcessor _processor;

    public ChunkingStrategyTests()
    {
        _processor = new DocumentProcessor(Mock.Of<ILogger<DocumentProcessor>>());
    }

    // --- Fixed (regression) ---

    [Fact]
    public void ChunkByFixed_ShortText_ProducesOneChunk()
    {
        var text = "Short document with a single paragraph.";
        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions { Strategy = ChunkingStrategy.Fixed, ChunkSize = 1000 });

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be(text.Trim());
    }

    [Fact]
    public void ChunkByFixed_LongText_SplitsAtChunkSize()
    {
        // Two paragraphs each ~600 chars — should split into two chunks with ChunkSize=1000
        var para1 = new string('A', 600);
        var para2 = new string('B', 600);
        var text = para1 + "\n\n" + para2;

        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Fixed,
            ChunkSize = 1000,
            ChunkOverlap = 0
        });

        chunks.Should().HaveCount(2);
    }

    // --- Sentence strategy ---

    [Fact]
    public void ChunkBySentence_SplitsAtSentenceBoundaries()
    {
        var text = "First sentence. Second sentence. Third sentence.";
        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Sentence,
            ChunkSize = 1000 // large enough to hold all sentences in one chunk
        });

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("First sentence");
        chunks[0].Content.Should().Contain("Third sentence");
    }

    [Fact]
    public void ChunkBySentence_OverflowsIntoNewChunk()
    {
        // 3 sentences of ~50 chars each; ChunkSize=80 → first chunk fits 1-2, second gets the rest
        var s1 = "This is the first long sentence here.";
        var s2 = "This is the second long sentence here.";
        var s3 = "This is the third long sentence here.";
        var text = $"{s1} {s2} {s3}";

        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Sentence,
            ChunkSize = 80
        });

        chunks.Should().HaveCountGreaterThan(1);
        // Every sentence must appear in at least one chunk
        var allContent = string.Join(" ", chunks.Select(c => c.Content));
        allContent.Should().Contain("first long sentence");
        allContent.Should().Contain("third long sentence");
    }

    [Fact]
    public void ChunkBySentence_SplitsAtExclamationAndQuestion()
    {
        var text = "Is this working? Yes it is! Great.";
        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions
        {
            Strategy = ChunkingStrategy.Sentence,
            ChunkSize = 1000
        });

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain("Is this working?");
    }

    [Fact]
    public void ChunkBySentence_EmptyText_ReturnsEmpty()
    {
        var chunks = _processor.ChunkText(Guid.NewGuid(), "   ", new ChunkingOptions { Strategy = ChunkingStrategy.Sentence });
        chunks.Should().BeEmpty();
    }

    // --- Paragraph strategy ---

    [Fact]
    public void ChunkByParagraph_EachParagraphIsOneChunk()
    {
        var text = "First paragraph content.\n\nSecond paragraph content.\n\nThird paragraph content.";

        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions { Strategy = ChunkingStrategy.Paragraph });

        chunks.Should().HaveCount(3);
        chunks[0].Content.Should().Be("First paragraph content.");
        chunks[1].Content.Should().Be("Second paragraph content.");
        chunks[2].Content.Should().Be("Third paragraph content.");
    }

    [Fact]
    public void ChunkByParagraph_SkipsWhitespaceOnlyParagraphs()
    {
        var text = "Real paragraph.\n\n   \n\nAnother real paragraph.";

        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions { Strategy = ChunkingStrategy.Paragraph });

        chunks.Should().HaveCount(2);
        chunks.Should().NotContain(c => string.IsNullOrWhiteSpace(c.Content));
    }

    [Fact]
    public void ChunkByParagraph_SingleParagraph_ReturnsOneChunk()
    {
        var text = "One and only paragraph without any blank lines.";

        var chunks = _processor.ChunkText(Guid.NewGuid(), text, new ChunkingOptions { Strategy = ChunkingStrategy.Paragraph });

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Be(text);
    }

    // --- Controller validation ---

    [Fact]
    public async Task UploadDocument_InvalidChunkingStrategy_Returns400()
    {
        // Arrange
        var processorMock = new Mock<IDocumentProcessor>();
        processorMock.Setup(p => p.IsSupported(It.IsAny<string>())).Returns(true);
        processorMock.Setup(p => p.SupportedContentTypes).Returns(new[] { "text/plain" });

        var documentService = new DocumentService(
            processorMock.Object,
            Mock.Of<IEmbeddingService>(),
            Mock.Of<IVectorStore>(),
            Mock.Of<ILogger<DocumentService>>(),
            Mock.Of<IDocumentRepository>(),
            Options.Create(new DocumentProcessingOptions()));

        var controller = new DocumentsController(documentService);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(10);
        fileMock.Setup(f => f.ContentType).Returns("text/plain");
        fileMock.Setup(f => f.FileName).Returns("test.txt");
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[10]));

        // Act
        var result = await controller.UploadDocument(fileMock.Object, null, "NotAValidStrategy", CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result;
        bad.Value.ToString().Should().Contain("NotAValidStrategy");
    }
}

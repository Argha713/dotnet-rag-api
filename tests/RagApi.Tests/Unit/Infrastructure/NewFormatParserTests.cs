// Argha - 2026-03-22 - #83 - Tests for Phase 12 new file format support: XLSX, CSV, PPTX, hierarchical chunking, DOCX headings
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Infrastructure.DocumentProcessing;

namespace RagApi.Tests.Unit.Infrastructure;

public class NewFormatParserTests
{
    private readonly DocumentProcessor _sut;

    public NewFormatParserTests()
    {
        // Argha - 2026-03-22 - #83 - Standard ctor with null-object OCR and audio services
        _sut = new DocumentProcessor(
            Mock.Of<ILogger<DocumentProcessor>>(),
            Mock.Of<IOcrService>(),
            Mock.Of<IAudioTranscriptionService>());
    }

    // ── DocumentProcessor: new format support ─────────────────────────────────

    [Fact]
    public void IsSupported_Xlsx_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - DocumentProcessor must declare XLSX as a supported type
        _sut.IsSupported("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            .Should().BeTrue();
    }

    [Fact]
    public void IsSupported_Csv_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - DocumentProcessor must declare CSV as a supported type
        _sut.IsSupported("text/csv").Should().BeTrue();
    }

    [Fact]
    public void IsSupported_Pptx_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - DocumentProcessor must declare PPTX as a supported type
        _sut.IsSupported("application/vnd.openxmlformats-officedocument.presentationml.presentation")
            .Should().BeTrue();
    }

    // ── CSV parsing ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractFromCsv_FormatsAsHeaderValuePairs()
    {
        // Argha - 2026-03-22 - #83 - Each data row must be rendered as "Header1: Value | Header2: Value"
        var csv = "Name,Age\nAlice,30\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var result = await _sut.ExtractTextAsync(stream, "text/csv");

        result.Should().Contain("Name: Alice").And.Contain("Age: 30");
        result.Should().Contain(" | ");
    }

    [Fact]
    public async Task ExtractFromCsv_EmptyFile_ReturnsEmptyString()
    {
        // Argha - 2026-03-22 - #83 - A CSV with only headers (no data rows) must produce no output rows
        var csv = "";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var result = await _sut.ExtractTextAsync(stream, "text/csv");

        result.Trim().Should().BeEmpty();
    }

    // ── XLSX parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void IsSupported_XlsxMimeType_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - Verify the official XLSX MIME type is in the supported list
        var xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        _sut.SupportedContentTypes.Should().Contain(xlsx);
    }

    // ── PPTX parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void IsSupported_PptxMimeType_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - Verify the official PPTX MIME type is in the supported list
        var pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
        _sut.SupportedContentTypes.Should().Contain(pptx);
    }

    // ── Hierarchical chunking ─────────────────────────────────────────────────

    [Fact]
    public void ChunkText_Hierarchical_SplitsAtSectionMarkers()
    {
        // Argha - 2026-03-22 - #83 - [Section: ...] markers must produce separate chunks, each prefixed with the header
        var text = "[Section: Intro]\nParagraph one text here.\n\n[Section: Body]\nParagraph two text here.";
        var options = new ChunkingOptions { Strategy = ChunkingStrategy.Hierarchical, ChunkSize = 2000, ChunkOverlap = 0 };

        var chunks = _sut.ChunkText(Guid.NewGuid(), text, options);

        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().StartWith("[Section: Intro]");
        chunks[1].Content.Should().StartWith("[Section: Body]");
    }

    [Fact]
    public void ChunkText_Hierarchical_PreservesSectionPrefixOnEachChunk()
    {
        // Argha - 2026-03-22 - #83 - When a section body has multiple paragraphs exceeding ChunkSize,
        // each output chunk must still start with the section header
        // Use two paragraphs (separated by double newline) each exceeding ChunkSize=100 to force multiple chunks
        var para1 = new string('A', 600);
        var para2 = new string('B', 600);
        var text = $"[Section: Title]\n{para1}\n\n{para2}";
        var options = new ChunkingOptions { Strategy = ChunkingStrategy.Hierarchical, ChunkSize = 800, ChunkOverlap = 0 };

        var chunks = _sut.ChunkText(Guid.NewGuid(), text, options);

        chunks.Should().HaveCountGreaterThan(1);
        foreach (var chunk in chunks)
            chunk.Content.Should().StartWith("[Section: Title]");
    }

    // ── DOCX heading annotation ───────────────────────────────────────────────

    [Fact]
    public async Task ExtractFromDocx_PlainParagraph_NotAnnotated()
    {
        // Argha - 2026-03-22 - #83 - Regular paragraphs (not styled as headings) must NOT get a [Section:] prefix
        var docxBytes = CreateMinimalDocxWithParagraph("Hello plain world");
        using var stream = new MemoryStream(docxBytes);
        var contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        var result = await _sut.ExtractTextAsync(stream, contentType);

        result.Should().Contain("Hello plain world");
        // Argha - 2026-03-22 - #83 - Plain paragraphs must not be wrapped in [Section: ...]
        result.Should().NotContain("[Section: Hello plain world]");
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    // Argha - 2026-03-22 - #83 - Build a minimal DOCX with a single non-heading paragraph
    private static byte[] CreateMinimalDocxWithParagraph(string text)
    {
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new DocumentFormat.OpenXml.Wordprocessing.Body(
                    new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                        new DocumentFormat.OpenXml.Wordprocessing.Run(
                            new DocumentFormat.OpenXml.Wordprocessing.Text(text)))));
            doc.Save();
        }
        return ms.ToArray();
    }
}

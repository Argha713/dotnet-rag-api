using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Infrastructure.DocumentProcessing;
using UglyToad.PdfPig.Writer;

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

    // Argha - 2026-03-16 - #34 - ExtractImagesAsync tests: surface area and fast-path coverage

    [Fact]
    public async Task ExtractImagesAsync_PlainTextContentType_ReturnsEmpty()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));
        var result = await _sut.ExtractImagesAsync(stream, "text/plain");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxContentType_ReturnsEmpty()
    {
        // Argha - 2026-03-16 - #35 - Valid empty DOCX (no image parts) must return empty list
        using var stream = new MemoryStream(CreateEmptyDocx());
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithPng100x100_ReturnsOneImage()
    {
        // Argha - 2026-03-16 - #35 - 100x100 PNG meets minimum threshold; must be returned
        var pngBytes = MakeFakePngHeader(100, 100);
        using var stream = new MemoryStream(CreateDocxWithImage(pngBytes, "image/png"));
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().HaveCount(1);
        result[0].MimeType.Should().Be("image/png");
        result[0].WidthPx.Should().Be(100);
        result[0].HeightPx.Should().Be(100);
        result[0].PageNumber.Should().Be(0);
        result[0].ImageIndex.Should().Be(0);
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithSmallPng_IsSkipped()
    {
        // Argha - 2026-03-16 - #35 - 50x50 PNG is below the 100x100 threshold; must be skipped
        var smallPngBytes = MakeFakePngHeader(50, 50);
        using var stream = new MemoryStream(CreateDocxWithImage(smallPngBytes, "image/png"));
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithGif_HasGifMimeType()
    {
        // Argha - 2026-03-16 - #35 - GIF images must be included and carry image/gif MIME type
        var gifBytes = MakeFakeGifHeader(200, 150);
        using var stream = new MemoryStream(CreateDocxWithImage(gifBytes, "image/gif"));
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().HaveCount(1);
        result[0].MimeType.Should().Be("image/gif");
        result[0].WidthPx.Should().Be(200);
        result[0].HeightPx.Should().Be(150);
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithUnreadableDimensions_IsIncluded()
    {
        // Argha - 2026-03-16 - #35 - BMP has no dimension reader; must be included (fail-open)
        var bmpBytes = new byte[] { 0x42, 0x4D, 0x00, 0x00 };
        using var stream = new MemoryStream(CreateDocxWithImage(bmpBytes, "image/bmp"));
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().HaveCount(1);
        result[0].WidthPx.Should().Be(0);
        result[0].HeightPx.Should().Be(0);
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithMultipleImages_HasSequentialIndices()
    {
        // Argha - 2026-03-16 - #35 - Two 100x100 PNGs must get ImageIndex 0 and 1 respectively
        var png1 = MakeFakePngHeader(100, 100);
        var png2 = MakeFakePngHeader(200, 200);

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph()));
            foreach (var imgBytes in new[] { png1, png2 })
            {
                var part = main.AddImagePart("image/png");
                using var imgMs = new MemoryStream(imgBytes);
                part.FeedData(imgMs);
            }
            doc.Save();
        }

        using var stream = new MemoryStream(ms.ToArray());
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        result.Should().HaveCount(2);
        result[0].ImageIndex.Should().Be(0);
        result[1].ImageIndex.Should().Be(1);
    }

    [Fact]
    public async Task ExtractImagesAsync_DocxWithOversizedImage_IsSkipped()
    {
        // Argha - 2026-03-16 - #35 - Image just over 20MB must be rejected by the safety limit
        var oversizedBytes = new byte[20 * 1024 * 1024 + 1];
        var header = MakeFakePngHeader(500, 500);
        Array.Copy(header, oversizedBytes, header.Length);

        using var stream = new MemoryStream(CreateDocxWithImage(oversizedBytes, "image/png"));
        var result = await _sut.ExtractImagesAsync(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.Should().BeEmpty();
    }

    // Argha - 2026-03-16 - #35 - DOCX test helpers

    private static byte[] CreateEmptyDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph()));
            doc.Save();
        }
        return ms.ToArray();
    }

    private static byte[] CreateDocxWithImage(byte[] imageBytes, string contentType)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph()));
            var part = main.AddImagePart(contentType);
            using var imgMs = new MemoryStream(imageBytes);
            part.FeedData(imgMs);
            doc.Save();
        }
        return ms.ToArray();
    }

    private static byte[] MakeFakePngHeader(int width, int height)
    {
        var b = new byte[32];
        b[0]=0x89; b[1]=0x50; b[2]=0x4E; b[3]=0x47;
        b[4]=0x0D; b[5]=0x0A; b[6]=0x1A; b[7]=0x0A;
        b[8]=0; b[9]=0; b[10]=0; b[11]=13;
        b[12]=0x49; b[13]=0x48; b[14]=0x44; b[15]=0x52;
        b[16]=(byte)(width>>24); b[17]=(byte)(width>>16); b[18]=(byte)(width>>8); b[19]=(byte)width;
        b[20]=(byte)(height>>24); b[21]=(byte)(height>>16); b[22]=(byte)(height>>8); b[23]=(byte)height;
        return b;
    }

    private static byte[] MakeFakeGifHeader(int width, int height)
    {
        var b = new byte[10];
        b[0]=0x47; b[1]=0x49; b[2]=0x46;
        b[3]=0x38; b[4]=0x39; b[5]=0x61;
        b[6]=(byte)(width&0xFF); b[7]=(byte)(width>>8);
        b[8]=(byte)(height&0xFF); b[9]=(byte)(height>>8);
        return b;
    }

    [Fact]
    public async Task ExtractImagesAsync_TextOnlyPdf_ReturnsEmpty()
    {
        // Argha - 2026-03-16 - #34 - Build a minimal single-page text-only PDF with no embedded images
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);
        var result = await _sut.ExtractImagesAsync(stream, "application/pdf");

        result.Should().BeEmpty();
    }

    // Argha - 2026-03-18 - #52 - VisionOptions default cost-guard ceiling
    [Fact]
    public void VisionOptions_DefaultMaxImagesPerDocument_IsTwenty()
    {
        var options = new VisionOptions();
        options.MaxImagesPerDocument.Should().Be(20);
    }
}

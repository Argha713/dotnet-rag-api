using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace RagApi.Infrastructure.DocumentProcessing;

/// <summary>
/// Service for processing and chunking documents
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ILogger<DocumentProcessor> _logger;

    private static readonly IReadOnlyList<string> _supportedTypes = new[]
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain",
        "text/markdown"
    };

    public DocumentProcessor(ILogger<DocumentProcessor> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedContentTypes => _supportedTypes;

    public bool IsSupported(string contentType)
    {
        return _supportedTypes.Contains(contentType.ToLowerInvariant());
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ExtractFromPdf(fileStream),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractFromDocx(fileStream),
            "text/plain" or "text/markdown" => await ExtractFromTextAsync(fileStream, cancellationToken),
            _ => throw new NotSupportedException($"Content type '{contentType}' is not supported")
        };
    }

    public List<DocumentChunk> ChunkText(Guid documentId, string text, ChunkingOptions? options = null)
    {
        options ??= new ChunkingOptions();

        // Clean the text
        text = CleanText(text);

        if (string.IsNullOrWhiteSpace(text))
            return new List<DocumentChunk>();

        // Argha - 2026-02-20 - Dispatch to the appropriate strategy 
        var chunks = options.Strategy switch
        {
            ChunkingStrategy.Sentence => ChunkBySentence(documentId, text, options),
            ChunkingStrategy.Paragraph => ChunkByParagraph(documentId, text),
            _ => ChunkByFixed(documentId, text, options)
        };

        _logger.LogDebug("Created {ChunkCount} chunks (strategy={Strategy}) from text of length {TextLength}",
            chunks.Count, options.Strategy, text.Length);

        return chunks;
    }

    // Argha - 2026-02-20 - Existing paragraph-aware fixed-size chunking, now extracted to a private method 
    private static List<DocumentChunk> ChunkByFixed(Guid documentId, string text, ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();
        var paragraphs = Regex.Split(text, options.SeparatorPattern)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        var currentChunk = new StringBuilder();
        var currentPosition = 0;
        var chunkIndex = 0;
        var chunkStartPosition = 0;

        foreach (var paragraph in paragraphs)
        {
            // Argha - 2026-02-15 - If adding this paragraph would exceed chunk size, save current chunk
            if (currentChunk.Length > 0 &&
                currentChunk.Length + paragraph.Length > options.ChunkSize)
            {
                chunks.Add(CreateChunk(
                    documentId,
                    currentChunk.ToString().Trim(),
                    chunkIndex++,
                    chunkStartPosition,
                    currentPosition));

                // Start new chunk with overlap
                var overlapText = GetOverlapText(currentChunk.ToString(), options.ChunkOverlap);
                currentChunk.Clear();
                currentChunk.Append(overlapText);
                chunkStartPosition = currentPosition - overlapText.Length;
            }

            currentChunk.AppendLine(paragraph);
            currentPosition += paragraph.Length + 2; // +2 for newline
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(CreateChunk(
                documentId,
                currentChunk.ToString().Trim(),
                chunkIndex,
                chunkStartPosition,
                currentPosition));
        }

        return chunks;
    }

    // Argha - 2026-02-20 - Sentence-based chunking: splits at .!? boundaries, groups into size-limited chunks 
    private static List<DocumentChunk> ChunkBySentence(Guid documentId, string text, ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();

        // Split into sentences at . ! ? followed by whitespace or end of string
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sentences.Count == 0)
            return chunks;

        var currentChunk = new StringBuilder();
        var chunkIndex = 0;
        var position = 0;
        var chunkStartPosition = 0;
        var lastSentence = string.Empty; // Argha - 2026-02-20 - One-sentence overlap 

        foreach (var sentence in sentences)
        {
            // Argha - 2026-02-20 - When adding this sentence would overflow, flush current chunk 
            if (currentChunk.Length > 0 &&
                currentChunk.Length + sentence.Length + 1 > options.ChunkSize)
            {
                chunks.Add(CreateChunk(
                    documentId,
                    currentChunk.ToString().Trim(),
                    chunkIndex++,
                    chunkStartPosition,
                    position));

                // Start next chunk with the last sentence as overlap
                currentChunk.Clear();
                if (!string.IsNullOrWhiteSpace(lastSentence))
                {
                    currentChunk.Append(lastSentence).Append(' ');
                    chunkStartPosition = position - lastSentence.Length - 1;
                }
                else
                {
                    chunkStartPosition = position;
                }
            }

            lastSentence = sentence;
            currentChunk.Append(sentence).Append(' ');
            position += sentence.Length + 1;
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(CreateChunk(
                documentId,
                currentChunk.ToString().Trim(),
                chunkIndex,
                chunkStartPosition,
                position));
        }

        return chunks;
    }

    // Argha - 2026-02-20 - Paragraph-based chunking: each blank-line-separated block is one chunk 
    private static List<DocumentChunk> ChunkByParagraph(Guid documentId, string text)
    {
        var paragraphs = Regex.Split(text, @"\n\n|\r\n\r\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        var chunks = new List<DocumentChunk>();
        var position = 0;

        for (int i = 0; i < paragraphs.Count; i++)
        {
            var para = paragraphs[i];
            chunks.Add(CreateChunk(documentId, para, i, position, position + para.Length));
            position += para.Length + 2; // +2 for the blank-line separator
        }

        return chunks;
    }

    private static DocumentChunk CreateChunk(
        Guid documentId, 
        string content, 
        int index, 
        int startPos, 
        int endPos)
    {
        return new DocumentChunk
        {
            DocumentId = documentId,
            Content = content,
            ChunkIndex = index,
            StartPosition = startPos,
            EndPosition = endPos
        };
    }

    private static string GetOverlapText(string text, int overlapSize)
    {
        if (text.Length <= overlapSize)
            return text;

        // Try to break at a word boundary
        var startIndex = text.Length - overlapSize;
        var spaceIndex = text.IndexOf(' ', startIndex);
        
        if (spaceIndex > startIndex && spaceIndex < text.Length - 10)
        {
            return text.Substring(spaceIndex + 1);
        }

        return text.Substring(startIndex);
    }

    private static string CleanText(string text)
    {
        // Remove excessive whitespace
        text = Regex.Replace(text, @"[ \t]+", " ");
        // Normalize line endings
        text = Regex.Replace(text, @"\r\n|\r", "\n");
        // Remove more than 2 consecutive newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private string ExtractFromPdf(Stream fileStream)
    {
        var textBuilder = new StringBuilder();

        using var document = PdfDocument.Open(fileStream);
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            textBuilder.AppendLine(pageText);
            textBuilder.AppendLine(); // Add paragraph break between pages
        }

        return textBuilder.ToString();
    }

    private string ExtractFromDocx(Stream fileStream)
    {
        var textBuilder = new StringBuilder();

        using var document = WordprocessingDocument.Open(fileStream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        
        if (body == null)
        {
            return string.Empty;
        }

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                textBuilder.AppendLine(text);
            }
        }

        return textBuilder.ToString();
    }

    private static async Task<string> ExtractFromTextAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(fileStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    // Argha - 2026-03-16 - #34 - Extract images from a document; dispatches to PDF or DOCX handler
    public Task<List<ExtractedImage>> ExtractImagesAsync(Stream fileStream, string contentType, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #35 - DOCX branch; must be checked before the PDF guard
        if (contentType.Equals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StringComparison.OrdinalIgnoreCase))
            return ExtractImagesFromDocxAsync(fileStream);

        if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new List<ExtractedImage>());

        var results = new List<ExtractedImage>();

        using var document = PdfDocument.Open(fileStream);
        var pageNumber = 0;

        foreach (var page in document.GetPages())
        {
            pageNumber++;
            var imageIndex = 0;

            foreach (var image in page.GetImages())
            {
                // Argha - 2026-03-16 - #34 - Skip decorative/icon images below minimum dimension threshold
                if (image.WidthInSamples < 100 || image.HeightInSamples < 100)
                    continue;

                // Argha - 2026-03-16 - #34 - Determine filter; skip unsupported compression formats
                var filterName = GetImageFilterName(image);
                if (filterName is "JBIG2Decode" or "CCITTFaxDecode")
                    continue;

                byte[] bytes;
                string mimeType;

                if (filterName == "DCTDecode")
                {
                    // Argha - 2026-03-16 - #34 - DCTDecode = JPEG; RawBytes holds the encoded JPEG data
                    bytes = image.RawBytes.ToArray();
                    mimeType = "image/jpeg";
                }
                else
                {
                    // Argha - 2026-03-16 - #34 - All other encodings: attempt conversion to PNG via PdfPig
                    if (!image.TryGetPng(out var png) || png is null)
                        continue;

                    bytes = png;
                    mimeType = "image/png";
                }

                // Argha - 2026-03-16 - #34 - Skip images exceeding 20MB safety limit
                const int MaxBytes = 20 * 1024 * 1024;
                if (bytes.Length > MaxBytes)
                    continue;

                results.Add(new ExtractedImage(
                    PageNumber: pageNumber,
                    ImageIndex: imageIndex,
                    Bytes: bytes,
                    MimeType: mimeType,
                    WidthPx: image.WidthInSamples,
                    HeightPx: image.HeightInSamples));

                imageIndex++;
            }
        }

        return Task.FromResult(results);
    }

    // Argha - 2026-03-16 - #34 - Read the first filter name from ImageDictionary; returns null when no filter key exists
    private static string? GetImageFilterName(IPdfImage image)
    {
        var dict = image.ImageDictionary;

        if (!dict.TryGet(NameToken.Filter, out IToken? filterToken))
            return null;

        // Argha - 2026-03-16 - #34 - Filter may be a single name or an array; read the first entry
        if (filterToken is NameToken nameTok)
            return nameTok.Data;

        if (filterToken is ArrayToken arrayTok && arrayTok.Data.Count > 0 && arrayTok.Data[0] is NameToken firstNameTok)
            return firstNameTok.Data;

        return null;
    }

    // Argha - 2026-03-16 - #35 - Enumerate MainDocumentPart.ImageParts; DOCX images have no page numbers (PageNumber = 0)
    private Task<List<ExtractedImage>> ExtractImagesFromDocxAsync(Stream fileStream)
    {
        var results = new List<ExtractedImage>();

        using var document = WordprocessingDocument.Open(fileStream, false);
        var mainPart = document.MainDocumentPart;
        if (mainPart == null)
            return Task.FromResult(results);

        var imageIndex = 0;
        const int MaxBytes = 20 * 1024 * 1024;

        foreach (var imagePart in mainPart.ImageParts)
        {
            var mimeType = imagePart.ContentType;

            byte[] bytes;
            using (var imgStream = imagePart.GetStream())
            using (var ms = new MemoryStream())
            {
                imgStream.CopyTo(ms);
                bytes = ms.ToArray();
            }

            // Argha - 2026-03-16 - #35 - Reject images exceeding 20MB safety limit
            if (bytes.Length > MaxBytes)
                continue;

            // Argha - 2026-03-16 - #35 - Apply 100x100 filter when dimensions are readable; include if unreadable
            var (width, height) = TryGetImageDimensions(bytes, mimeType);
            if (width.HasValue && height.HasValue && (width.Value < 100 || height.Value < 100))
                continue;

            results.Add(new ExtractedImage(
                PageNumber: 0,
                ImageIndex: imageIndex,
                Bytes: bytes,
                MimeType: mimeType,
                WidthPx: width ?? 0,
                HeightPx: height ?? 0));

            imageIndex++;
        }

        return Task.FromResult(results);
    }

    // Argha - 2026-03-16 - #35 - Dispatch dimension reading by MIME type; returns (null,null) for unrecognised formats
    private static (int? Width, int? Height) TryGetImageDimensions(byte[] bytes, string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/png"                => ReadPngDimensions(bytes),
            "image/jpeg" or "image/jpg" => ReadJpegDimensions(bytes),
            "image/gif"                => ReadGifDimensions(bytes),
            _                          => (null, null)
        };

    // Argha - 2026-03-16 - #35 - PNG IHDR: bytes 16–19 = width BE, bytes 20–23 = height BE
    private static (int? Width, int? Height) ReadPngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            return (null, null);

        var w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (w, h);
    }

    // Argha - 2026-03-16 - #35 - JPEG: scan for SOF0 (FF C0) or SOF2 (FF C2); height at marker+5, width at marker+7
    private static (int? Width, int? Height) ReadJpegDimensions(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return (null, null);

        var i = 2;
        while (i + 8 < bytes.Length)
        {
            if (bytes[i] != 0xFF) break;
            var marker = bytes[i + 1];
            var segLen = (bytes[i + 2] << 8) | bytes[i + 3];

            if (marker == 0xC0 || marker == 0xC2)
            {
                var h = (bytes[i + 5] << 8) | bytes[i + 6];
                var w = (bytes[i + 7] << 8) | bytes[i + 8];
                return (w, h);
            }
            i += 2 + segLen;
        }
        return (null, null);
    }

    // Argha - 2026-03-16 - #35 - GIF header: bytes 6–7 = width LE, bytes 8–9 = height LE
    private static (int? Width, int? Height) ReadGifDimensions(byte[] bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 0x47 || bytes[1] != 0x49 || bytes[2] != 0x46)
            return (null, null);

        var w = bytes[6] | (bytes[7] << 8);
        var h = bytes[8] | (bytes[9] << 8);
        return (w, h);
    }
}

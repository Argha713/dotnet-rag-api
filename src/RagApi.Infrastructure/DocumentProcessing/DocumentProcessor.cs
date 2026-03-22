using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
// Argha - 2026-03-20 - #51 - IOcrService used for OCR fallback on scanned PDFs
using Page = UglyToad.PdfPig.Content.Page;

namespace RagApi.Infrastructure.DocumentProcessing;

/// <summary>
/// Service for processing and chunking documents
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ILogger<DocumentProcessor> _logger;
    // Argha - 2026-03-20 - #51 - OCR service injected for scanned PDF fallback; NullOcrService when disabled
    private readonly IOcrService _ocrService;
    // Argha - 2026-03-22 - #81 - Audio transcription service; NullAudioTranscriptionService when provider is Ollama
    private readonly IAudioTranscriptionService _audioTranscriptionService;

    // Argha - 2026-03-22 - #75 - Extended with XLSX, CSV, PPTX, and audio MIME types
    private static readonly IReadOnlyList<string> _supportedTypes = new[]
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain",
        "text/markdown",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "audio/mpeg",
        "audio/wav",
        "audio/mp4"
    };

    // Argha - 2026-03-22 - #81 - Added IAudioTranscriptionService as second parameter after IOcrService
    public DocumentProcessor(ILogger<DocumentProcessor> logger, IOcrService ocrService, IAudioTranscriptionService audioTranscriptionService)
    {
        _logger = logger;
        _ocrService = ocrService;
        _audioTranscriptionService = audioTranscriptionService;
    }

    public IReadOnlyList<string> SupportedContentTypes => _supportedTypes;

    public bool IsSupported(string contentType)
    {
        return _supportedTypes.Contains(contentType.ToLowerInvariant());
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-20 - #51 - PDF branch is now async to support OCR fallback
        // Argha - 2026-03-22 - #75 - Added XLSX, CSV, PPTX, and audio cases
        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => await ExtractFromPdfAsync(fileStream, cancellationToken),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractFromDocx(fileStream),
            "text/plain" or "text/markdown" => await ExtractFromTextAsync(fileStream, cancellationToken),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => await ExcelCsvParser.ExtractFromXlsxAsync(fileStream, cancellationToken),
            "text/csv" => await ExcelCsvParser.ExtractFromCsvAsync(fileStream, cancellationToken),
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => await PptxParser.ExtractFromPptxAsync(fileStream, cancellationToken),
            "audio/mpeg" or "audio/wav" or "audio/mp4" => await TranscribeAudioAsync(fileStream, contentType, cancellationToken),
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
        // Argha - 2026-03-22 - #77 - Added Hierarchical case
        var chunks = options.Strategy switch
        {
            ChunkingStrategy.Sentence => ChunkBySentence(documentId, text, options),
            ChunkingStrategy.Paragraph => ChunkByParagraph(documentId, text),
            ChunkingStrategy.Hierarchical => ChunkByHierarchical(documentId, text, options),
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

    // Argha - 2026-03-22 - #77 - Hierarchical chunking: splits at [Section: ...] markers; preserves section prefix on each output chunk
    private static List<DocumentChunk> ChunkByHierarchical(Guid documentId, string text, ChunkingOptions options)
    {
        var chunks = new List<DocumentChunk>();

        // Argha - 2026-03-22 - #77 - Split text at every line that starts with [Section:
        var sectionPattern = new Regex(@"(?=^\[Section:)", RegexOptions.Multiline);
        var sections = sectionPattern.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var chunkIndex = 0;

        foreach (var section in sections)
        {
            var lines = section.Split('\n');
            var sectionHeader = lines[0].Trim(); // e.g. "[Section: Introduction]"
            var sectionBody = string.Join("\n", lines.Skip(1)).Trim();

            if (string.IsNullOrWhiteSpace(sectionBody))
            {
                // Argha - 2026-03-22 - #77 - Section with no body text: emit just the header as a single chunk
                chunks.Add(CreateChunk(documentId, sectionHeader, chunkIndex++, 0, sectionHeader.Length));
                continue;
            }

            // Argha - 2026-03-22 - #77 - Split the body using fixed chunking, then prepend the section header to each chunk
            var bodyOptions = new ChunkingOptions
            {
                ChunkSize = options.ChunkSize,
                ChunkOverlap = options.ChunkOverlap,
                Strategy = ChunkingStrategy.Fixed
            };

            var bodyChunks = ChunkByFixed(documentId, sectionBody, bodyOptions);
            foreach (var bodyChunk in bodyChunks)
            {
                var content = $"{sectionHeader}\n{bodyChunk.Content}";
                chunks.Add(CreateChunk(documentId, content, chunkIndex++, 0, content.Length));
            }
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

    // Argha - 2026-03-20 - #51 - Async; falls back to OCR when PdfPig yields no text (scanned PDFs)
    private async Task<string> ExtractFromPdfAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(fileStream);
        var pages = document.GetPages().ToList();

        var textBuilder = new StringBuilder();
        foreach (var page in pages)
        {
            textBuilder.AppendLine(page.Text);

            // Argha - 2026-03-22 - #76 - Append any detected tables as Markdown after the page's regular text
            var tableText = ExtractTablesFromPage(page);
            if (!string.IsNullOrWhiteSpace(tableText))
                textBuilder.AppendLine(tableText);

            textBuilder.AppendLine(); // Add paragraph break between pages
        }

        var text = textBuilder.ToString();

        // Argha - 2026-03-20 - #51 - If PdfPig found no text and OCR is enabled, extract page images and OCR them
        if (!string.IsNullOrWhiteSpace(text) || !_ocrService.IsEnabled)
            return text;

        _logger.LogInformation("PDF text layer empty; running OCR fallback via Tesseract");
        return await OcrPdfPagesAsync(pages, cancellationToken);
    }

    // Argha - 2026-03-22 - #76 - Detect table-like word clusters on a PDF page using Y/X position clustering
    // Groups words by approximate Y row (within 3-unit tolerance), then renders as Markdown
    private static string ExtractTablesFromPage(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
        if (words.Count < 4) return string.Empty;

        // Argha - 2026-03-22 - #76 - Group words by Y coordinate (bottom of bounding box) within tolerance
        const double RowTolerance = 3.0;
        var rows = new SortedDictionary<double, List<(double X, string Text)>>(Comparer<double>.Create((a, b) =>
        {
            // Argha - 2026-03-22 - #76 - PDF Y axis is bottom-up; larger Y = higher on page; sort descending
            return b.CompareTo(a);
        }));

        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            var x = word.BoundingBox.Left;

            var existingKey = rows.Keys.FirstOrDefault(k => Math.Abs(k - y) <= RowTolerance, double.NaN);
            if (!double.IsNaN(existingKey))
            {
                rows[existingKey].Add((x, word.Text));
            }
            else
            {
                rows[y] = new List<(double, string)> { (x, word.Text) };
            }
        }

        // Argha - 2026-03-22 - #76 - Only consider it a table if we have at least 2 rows each with 2+ cells
        var tableRows = rows.Values
            .Where(r => r.Count >= 2)
            .ToList();

        if (tableRows.Count < 2) return string.Empty;

        // Argha - 2026-03-22 - #76 - Render rows as Markdown table; first row = header
        var sb = new StringBuilder();
        var headerCells = tableRows[0].OrderBy(c => c.X).Select(c => c.Text).ToList();
        sb.AppendLine("| " + string.Join(" | ", headerCells) + " |");
        sb.AppendLine("| " + string.Join(" | ", headerCells.Select(_ => "---")) + " |");

        foreach (var row in tableRows.Skip(1))
        {
            var cells = row.OrderBy(c => c.X).Select(c => c.Text).ToList();
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }

        return sb.ToString();
    }

    // Argha - 2026-03-20 - #51 - Extracts embedded images from each page and runs OCR; concatenates results
    private async Task<string> OcrPdfPagesAsync(List<Page> pages, CancellationToken cancellationToken)
    {
        var ocrBuilder = new StringBuilder();

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var pageNumber = i + 1;
            var pageHadText = false;

            foreach (var image in page.GetImages())
            {
                // Argha - 2026-03-20 - #51 - No dimension filter here; the full page image is what we want
                var filterName = GetImageFilterName(image);

                // Argha - 2026-03-20 - #51 - JBIG2 has no free decoder; skip (see also ExtractImagesAsync)
                if (filterName is "JBIG2Decode")
                    continue;

                byte[] bytes;
                if (filterName == "DCTDecode")
                {
                    bytes = image.RawBytes.ToArray();
                }
                else
                {
                    if (!image.TryGetPng(out var png) || png is null)
                        continue;
                    bytes = png;
                }

                var pageText = await _ocrService.RecognizeTextAsync(bytes, cancellationToken);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    ocrBuilder.AppendLine(pageText);
                    pageHadText = true;
                }
            }

            if (pageHadText)
                ocrBuilder.AppendLine(); // paragraph break between pages
            else
                _logger.LogDebug("OCR found no text on page {PageNumber}", pageNumber);
        }

        return ocrBuilder.ToString();
    }

    // Argha - 2026-03-22 - #76 - Detect heading paragraphs by style ID; prefix with [Section: ...]
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
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Argha - 2026-03-22 - #76 - Detect heading paragraphs by style ID; prefix with [Section: ...]
            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
            if (styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase))
            {
                textBuilder.AppendLine($"[Section: {text}]");
            }
            else
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

    // Argha - 2026-03-22 - #81 - Delegates audio transcription to the injected service; throws NotSupportedException for Ollama
    private async Task<string> TranscribeAudioAsync(Stream stream, string mimeType, CancellationToken cancellationToken)
    {
        if (!_audioTranscriptionService.IsSupported)
            throw new NotSupportedException("Audio transcription requires an OpenAI or AzureOpenAI provider.");

        return await _audioTranscriptionService.TranscribeAsync(stream, mimeType, cancellationToken);
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
                // Argha - 2026-03-18 - #55 - Skip decorative/icon images below minimum dimension threshold (lowered from 100 to 50)
                if (image.WidthInSamples < 50 || image.HeightInSamples < 50)
                    continue;

                // Argha - 2026-03-18 - #52 - JBIG2 has no viable free .NET decoder; log and skip
                // CCITTFaxDecode is handled by PdfPig's internal CcittFaxDecodeFilter via TryGetPng()
                var filterName = GetImageFilterName(image);
                if (filterName is "JBIG2Decode")
                {
                    _logger.LogDebug(
                        "Skipping JBIG2Decode image on page {Page} — no free .NET decoder available",
                        pageNumber);
                    continue;
                }

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

            // Argha - 2026-03-18 - #55 - Apply 50x50 filter when dimensions are readable (lowered from 100); include if unreadable
            var (width, height) = TryGetImageDimensions(bytes, mimeType);
            if (width.HasValue && height.HasValue && (width.Value < 50 || height.Value < 50))
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

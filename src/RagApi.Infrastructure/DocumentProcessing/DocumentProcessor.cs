using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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
}

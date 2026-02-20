using System.Text.Json;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Argha - 2026-02-15 - Commented out: replaced in-memory storage with IDocumentRepository (Phase 1.3)
// using System.Collections.Concurrent;

namespace RagApi.Application.Services;

/// <summary>
/// Service for managing document uploads and processing
/// </summary>
public class DocumentService
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentService> _logger;
    private readonly IDocumentRepository _documentRepository;
    // Argha - 2026-02-20 - Default chunking options from config (Phase 3.3)
    private readonly DocumentProcessingOptions _processingOptions;

    // Argha - 2026-02-15 - Commented out: replaced with SQLite via IDocumentRepository (Phase 1.3)
    // private static readonly ConcurrentDictionary<Guid, Document> _documents = new();

    // Argha - 2026-02-20 - Added IOptions<DocumentProcessingOptions> for configurable chunking (Phase 3.3)
    public DocumentService(
        IDocumentProcessor documentProcessor,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<DocumentService> logger,
        IDocumentRepository documentRepository,
        IOptions<DocumentProcessingOptions> processingOptions)
    {
        _documentProcessor = documentProcessor;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
        _documentRepository = documentRepository;
        _processingOptions = processingOptions.Value;
    }

    /// <summary>
    /// Upload and process a document
    /// </summary>
    // Argha - 2026-02-19 - Added optional tags parameter for metadata filtering (Phase 2.3)
    // Argha - 2026-02-20 - Added optional chunkingStrategy parameter for configurable chunking (Phase 3.3)
    public async Task<Document> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        List<string>? tags = null,
        ChunkingStrategy? chunkingStrategy = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading document: {FileName} ({ContentType})", fileName, contentType);

        // Validate content type
        if (!_documentProcessor.IsSupported(contentType))
        {
            throw new NotSupportedException(
                $"Content type '{contentType}' is not supported. Supported types: {string.Join(", ", _documentProcessor.SupportedContentTypes)}");
        }

        var normalizedTags = tags ?? new List<string>();

        // Create document record
        var document = new Document
        {
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileStream.Length,
            Status = DocumentStatus.Processing,
            // Argha - 2026-02-19 - Persist tags as JSON (Phase 2.3)
            TagsJson = JsonSerializer.Serialize(normalizedTags)
        };
        // Argha - 2026-02-15 - Commented out: was in-memory storage (Phase 1.3)
        // _documents[document.Id] = document;
        await _documentRepository.AddAsync(document, cancellationToken);

        try
        {
            // Step 1: Extract text from document
            _logger.LogDebug("Extracting text from document {DocumentId}", document.Id);
            var text = await _documentProcessor.ExtractTextAsync(fileStream, contentType, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("No text content could be extracted from the document.");
            }

            _logger.LogDebug("Extracted {Length} characters from document", text.Length);

            // Step 2: Chunk the text
            // Argha - 2026-02-20 - Build ChunkingOptions from config defaults, override strategy if supplied (Phase 3.3)
            var effectiveStrategy = chunkingStrategy
                ?? (Enum.TryParse<ChunkingStrategy>(_processingOptions.DefaultChunkingStrategy, ignoreCase: true, out var parsed)
                    ? parsed
                    : ChunkingStrategy.Fixed);

            var chunkingOptions = new ChunkingOptions
            {
                ChunkSize = _processingOptions.ChunkSize,
                ChunkOverlap = _processingOptions.ChunkOverlap,
                Strategy = effectiveStrategy
            };

            var chunks = _documentProcessor.ChunkText(document.Id, text, chunkingOptions);
            _logger.LogDebug("Created {Count} chunks from document", chunks.Count);

            // Step 3: Generate embeddings for all chunks
            var chunkTexts = chunks.Select(c => c.Content).ToList();
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(chunkTexts, cancellationToken);

            // Assign embeddings to chunks
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Embedding = embeddings[i];
                chunks[i].Metadata["fileName"] = fileName;
                chunks[i].Metadata["contentType"] = contentType;
                // Argha - 2026-02-19 - Propagate tags to each chunk for Qdrant payload (Phase 2.3)
                chunks[i].Tags = normalizedTags;
            }

            // Step 4: Store chunks in vector database
            await _vectorStore.UpsertChunksAsync(chunks, cancellationToken);

            // Update document status
            document.Status = DocumentStatus.Completed;
            document.ChunkCount = chunks.Count;
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation(
                "Document {DocumentId} processed successfully. {ChunkCount} chunks stored.",
                document.Id, chunks.Count);

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document {DocumentId}", document.Id);
            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = ex.Message;
            await _documentRepository.UpdateAsync(document, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Get document by ID
    /// </summary>
    public async Task<Document?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-02-15 - Commented out: was in-memory lookup (Phase 1.3)
        // _documents.TryGetValue(documentId, out var document);
        // return Task.FromResult(document);
        return await _documentRepository.GetByIdAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// Get all documents, optionally filtered by a tag
    /// </summary>
    // Argha - 2026-02-19 - Added optional tag filter; filtering done in memory (Phase 2.3)
    public async Task<List<Document>> GetAllDocumentsAsync(string? tag = null, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-02-15 - Commented out: was in-memory list (Phase 1.3)
        // return Task.FromResult(_documents.Values.ToList());
        var all = await _documentRepository.GetAllAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(tag))
            return all;

        return all.Where(d =>
        {
            var docTags = JsonSerializer.Deserialize<List<string>>(d.TagsJson) ?? new List<string>();
            return docTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        }).ToList();
    }

    /// <summary>
    /// Delete a document and its chunks
    /// </summary>
    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting document {DocumentId}", documentId);

        // Remove chunks from vector store
        await _vectorStore.DeleteDocumentChunksAsync(documentId, cancellationToken);

        // Argha - 2026-02-15 - Commented out: was in-memory removal (Phase 1.3)
        // _documents.TryRemove(documentId, out _);
        await _documentRepository.DeleteAsync(documentId, cancellationToken);

        _logger.LogInformation("Document {DocumentId} deleted successfully", documentId);
    }

    /// <summary>
    /// Get supported file types
    /// </summary>
    public IReadOnlyList<string> GetSupportedContentTypes()
    {
        return _documentProcessor.SupportedContentTypes;
    }
}

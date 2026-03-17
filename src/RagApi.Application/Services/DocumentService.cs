using System.Text.Json;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// Argha - 2026-02-15 - Commented out: replaced in-memory storage with IDocumentRepository 
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
    // Argha - 2026-02-20 - Default chunking options from config
    private readonly DocumentProcessingOptions _processingOptions;

    // Argha - 2026-02-15 - Commented out: replaced with SQLite via IDocumentRepository
    // private static readonly ConcurrentDictionary<Guid, Document> _documents = new();

    // Argha - 2026-02-21 - Batch upload options for concurrency and file count limits
    private readonly BatchUploadOptions _batchOptions;

    // Argha - 2026-03-04 - #17 - Workspace context provides the collection name for vector store isolation
    private readonly IWorkspaceContext _workspaceContext;

    // Argha - 2026-03-17 - #36 - Vision service and image store; optional so existing tests compile unchanged
    private readonly IVisionService? _visionService;
    private readonly IImageStore? _imageStore;

    // Argha - 2026-02-20 - Added IOptions<DocumentProcessingOptions> for configurable chunking
    // Argha - 2026-02-21 - Added IOptions<BatchUploadOptions> for batch upload settings
    // Argha - 2026-03-04 - #17 - Added IWorkspaceContext for per-request collection name
    // Argha - 2026-03-17 - #36 - Added optional IVisionService + IImageStore for multimodal ingestion
    public DocumentService(
        IDocumentProcessor documentProcessor,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<DocumentService> logger,
        IDocumentRepository documentRepository,
        IOptions<DocumentProcessingOptions> processingOptions,
        IWorkspaceContext workspaceContext,
        IOptions<BatchUploadOptions>? batchOptions = null,
        IVisionService? visionService = null,
        IImageStore? imageStore = null)
    {
        _documentProcessor = documentProcessor;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
        _documentRepository = documentRepository;
        _processingOptions = processingOptions.Value;
        _workspaceContext = workspaceContext;
        // Argha - 2026-02-21 - Optional to avoid breaking existing test constructors that don't pass it
        _batchOptions = batchOptions?.Value ?? new BatchUploadOptions();
        _visionService = visionService;
        _imageStore = imageStore;
    }

    /// <summary>
    /// Upload and process a document
    /// </summary>
    // Argha - 2026-02-19 - Added optional tags parameter for metadata filtering 
    // Argha - 2026-02-20 - Added optional chunkingStrategy parameter for configurable chunking 
    public async Task<Document> UploadDocumentAsync(Stream fileStream, string fileName, string contentType, List<string>? tags = null, ChunkingStrategy? chunkingStrategy = null, CancellationToken cancellationToken = default)
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
            // Argha - 2026-02-19 - Persist tags as JSON 
            TagsJson = JsonSerializer.Serialize(normalizedTags)
        };
        // Argha - 2026-02-15 - Commented out: was in-memory storage 
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
            // Argha - 2026-02-20 - Build ChunkingOptions from config defaults, override strategy if supplied 
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
                // Argha - 2026-02-19 - Propagate tags to each chunk for Qdrant payload 
                chunks[i].Tags = normalizedTags;
            }

            // Step 4: Store chunks in vector database
            // Argha - 2026-03-04 - #17 - Use workspace's collection name for tenant isolation
            await _vectorStore.UpsertChunksAsync(_workspaceContext.Current.CollectionName, chunks, cancellationToken);

            // Argha - 2026-03-17 - #36 - Step 5 (best-effort): vision pipeline — describe and embed images
            var imageChunks = new List<DocumentChunk>();
            if (_visionService?.IsEnabled == true)
            {
                // Rewind: UploadDocumentAsync requires a seekable stream (MemoryStream from IFormFile satisfies this)
                fileStream.Position = 0;
                imageChunks = await RunVisionPipelineAsync(document.Id, fileName, contentType, normalizedTags, fileStream, cancellationToken);
            }

            if (imageChunks.Count > 0)
            {
                var descEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                    imageChunks.Select(c => c.Content).ToList(), cancellationToken);
                for (int i = 0; i < imageChunks.Count; i++)
                    imageChunks[i].Embedding = descEmbeddings[i];
                await _vectorStore.UpsertChunksAsync(_workspaceContext.Current.CollectionName, imageChunks, cancellationToken);
            }

            // Update document status
            document.Status = DocumentStatus.Completed;
            document.ChunkCount = chunks.Count + imageChunks.Count;
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation(
                "Document {DocumentId} processed successfully. {ChunkCount} text chunks, {ImageChunkCount} image chunks stored.",
                document.Id, chunks.Count, imageChunks.Count);

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

    // Argha - 2026-02-21 - Update an existing document: replace content and re-process from scratch 
    /// <summary>
    /// Replace an existing document's content and re-process it.
    /// Deletes old vector chunks, runs the full extract → chunk → embed → upsert pipeline,
    /// and updates the SQLite record. The document ID is preserved.
    /// Throws <see cref="KeyNotFoundException"/> if the document does not exist.
    /// Throws <see cref="NotSupportedException"/> if the new content type is unsupported.
    /// </summary>
    public async Task<Document> UpdateDocumentAsync(
        Guid documentId,
        Stream fileStream,
        string fileName,
        string contentType,
        List<string>? tags = null,
        ChunkingStrategy? chunkingStrategy = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating document {DocumentId}: {FileName} ({ContentType})", documentId, fileName, contentType);

        // Argha - 2026-02-21 - Fetch existing record first; 404 semantics handled by caller
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Document '{documentId}' not found.");

        // Argha - 2026-02-21 - Validate new content type before touching any state
        if (!_documentProcessor.IsSupported(contentType))
        {
            throw new NotSupportedException(
                $"Content type '{contentType}' is not supported. Supported types: {string.Join(", ", _documentProcessor.SupportedContentTypes)}");
        }

        var normalizedTags = tags ?? new List<string>();

        // Argha - 2026-02-21 - Mark as Processing immediately so callers know re-process is in flight
        document.Status = DocumentStatus.Processing;
        document.UpdatedAt = DateTime.UtcNow;
        await _documentRepository.UpdateAsync(document, cancellationToken);

        try
        {
            // Argha - 2026-03-17 - #36 - Remove stale image records before re-extracting
            if (_imageStore != null)
                await _imageStore.DeleteByDocumentAsync(documentId, cancellationToken);

            // Step 1: Remove old vectors so stale chunks don't remain in the index
            _logger.LogDebug("Deleting old chunks for document {DocumentId}", documentId);
            // Argha - 2026-03-04 - #17 - Use workspace's collection name for tenant isolation
            await _vectorStore.DeleteDocumentChunksAsync(_workspaceContext.Current.CollectionName, documentId, cancellationToken);

            // Step 2: Extract text from the new file
            _logger.LogDebug("Extracting text from updated document {DocumentId}", documentId);
            var text = await _documentProcessor.ExtractTextAsync(fileStream, contentType, cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("No text content could be extracted from the document.");
            }

            _logger.LogDebug("Extracted {Length} characters from updated document", text.Length);

            // Step 3: Chunk with the effective strategy
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

            var chunks = _documentProcessor.ChunkText(documentId, text, chunkingOptions);
            _logger.LogDebug("Created {Count} chunks from updated document", chunks.Count);

            // Step 4: Generate embeddings
            var chunkTexts = chunks.Select(c => c.Content).ToList();
            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(chunkTexts, cancellationToken);

            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Embedding = embeddings[i];
                chunks[i].Metadata["fileName"] = fileName;
                chunks[i].Metadata["contentType"] = contentType;
                chunks[i].Tags = normalizedTags;
            }

            // Step 5: Store new chunks
            // Argha - 2026-03-04 - #17 - Use workspace's collection name for tenant isolation
            await _vectorStore.UpsertChunksAsync(_workspaceContext.Current.CollectionName, chunks, cancellationToken);

            // Argha - 2026-03-17 - #36 - Step 5b (best-effort): vision pipeline for updated document
            var imageChunks = new List<DocumentChunk>();
            if (_visionService?.IsEnabled == true)
            {
                fileStream.Position = 0;
                imageChunks = await RunVisionPipelineAsync(documentId, fileName, contentType, normalizedTags, fileStream, cancellationToken);
            }

            if (imageChunks.Count > 0)
            {
                var descEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
                    imageChunks.Select(c => c.Content).ToList(), cancellationToken);
                for (int i = 0; i < imageChunks.Count; i++)
                    imageChunks[i].Embedding = descEmbeddings[i];
                await _vectorStore.UpsertChunksAsync(_workspaceContext.Current.CollectionName, imageChunks, cancellationToken);
            }

            // Step 6: Persist updated metadata
            document.FileName = fileName;
            document.ContentType = contentType;
            document.FileSize = fileStream.Length;
            document.TagsJson = JsonSerializer.Serialize(normalizedTags);
            document.Status = DocumentStatus.Completed;
            document.ChunkCount = chunks.Count + imageChunks.Count;
            document.UpdatedAt = DateTime.UtcNow;
            document.ErrorMessage = null;
            await _documentRepository.UpdateAsync(document, cancellationToken);

            _logger.LogInformation(
                "Document {DocumentId} re-processed successfully. {ChunkCount} text chunks, {ImageChunkCount} image chunks stored.",
                documentId, chunks.Count, imageChunks.Count);

            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-process document {DocumentId}", documentId);
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
        // Argha - 2026-02-15 - Commented out: was in-memory lookup 
        // _documents.TryGetValue(documentId, out var document);
        // return Task.FromResult(document);
        return await _documentRepository.GetByIdAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// Get all documents, optionally filtered by a tag
    /// </summary>
    // Argha - 2026-02-19 - Added optional tag filter; filtering done in memory 
    public async Task<List<Document>> GetAllDocumentsAsync(string? tag = null, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-02-15 - Commented out: was in-memory list 
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

        // Argha - 2026-03-17 - #36 - Remove image records before vector chunks
        if (_imageStore != null)
            await _imageStore.DeleteByDocumentAsync(documentId, cancellationToken);

        // Remove chunks from vector store
        // Argha - 2026-03-04 - #17 - Use workspace's collection name for tenant isolation
        await _vectorStore.DeleteDocumentChunksAsync(_workspaceContext.Current.CollectionName, documentId, cancellationToken);

        // Argha - 2026-02-15 - Commented out: was in-memory removal 
        // _documents.TryRemove(documentId, out _);
        await _documentRepository.DeleteAsync(documentId, cancellationToken);

        _logger.LogInformation("Document {DocumentId} deleted successfully", documentId);
    }

    // Argha - 2026-03-17 - #36 - Best-effort: describe each extracted image with the vision service,
    // persist bytes to the image store, and return one DocumentChunk per image.
    // Individual image failures are logged and skipped — the whole upload is never failed by vision errors.
    private async Task<List<DocumentChunk>> RunVisionPipelineAsync(
        Guid documentId,
        string fileName,
        string contentType,
        List<string> tags,
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        List<ExtractedImage> images;
        try
        {
            images = await _documentProcessor.ExtractImagesAsync(fileStream, contentType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vision pipeline: image extraction failed for document {DocumentId} — skipping", documentId);
            return new List<DocumentChunk>();
        }

        if (images.Count == 0)
            return new List<DocumentChunk>();

        _logger.LogInformation("Vision pipeline: {Count} images found in document {DocumentId}", images.Count, documentId);

        var chunks = new List<DocumentChunk>();
        int succeeded = 0;

        foreach (var image in images)
        {
            try
            {
                var description = await _visionService!.DescribeImageAsync(
                    image.Bytes, image.MimeType, context: fileName, ct: cancellationToken);

                var docImage = new DocumentImage
                {
                    DocumentId = documentId,
                    WorkspaceId = _workspaceContext.Current.Id,
                    PageNumber = image.PageNumber > 0 ? image.PageNumber : null,
                    ContentType = image.MimeType,
                    Data = image.Bytes,
                    AiDescription = description,
                    CreatedAt = DateTime.UtcNow
                };
                var imageId = await _imageStore!.SaveAsync(docImage, cancellationToken);

                var chunk = new DocumentChunk
                {
                    DocumentId = documentId,
                    Content = description,
                    ChunkIndex = image.ImageIndex,
                    IsImageChunk = true,
                    ImageId = imageId,
                    Tags = tags,
                    Metadata = new Dictionary<string, string>
                    {
                        ["fileName"] = fileName,
                        ["contentType"] = contentType,
                        ["isImage"] = "true",
                        ["imageId"] = imageId.ToString(),
                        ["pageNumber"] = image.PageNumber.ToString()
                    }
                };
                chunks.Add(chunk);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Vision pipeline: failed to process image {Index} (page {Page}) for document {DocumentId} — skipping",
                    image.ImageIndex, image.PageNumber, documentId);
            }
        }

        _logger.LogInformation(
            "Vision pipeline: {Succeeded}/{Total} images processed for document {DocumentId}",
            succeeded, images.Count, documentId);

        return chunks;
    }

    /// <summary>
    /// Get supported file types
    /// </summary>
    public IReadOnlyList<string> GetSupportedContentTypes()
    {
        return _documentProcessor.SupportedContentTypes;
    }

    // Argha - 2026-02-21 - Batch upload: process multiple files concurrently with bounded parallelism 
    /// <summary>
    /// Upload and process multiple documents concurrently.
    /// Partial failure is allowed — exceptions per file are caught and returned as failed results,
    /// not propagated to the caller.
    /// </summary>
    public async Task<List<BatchUploadItemResult>> BatchUploadDocumentsAsync(
        List<(Stream Stream, string FileName, string ContentType)> files,
        List<string>? tags,
        ChunkingStrategy? chunkingStrategy,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
            return new List<BatchUploadItemResult>();

        _logger.LogInformation(
            "Starting batch upload of {Count} files (maxConcurrency={Max})",
            files.Count, _batchOptions.MaxConcurrency);

        // Argha - 2026-02-21 - SemaphoreSlim limits concurrent embedding calls to avoid overwhelming the AI service
        var semaphore = new SemaphoreSlim(_batchOptions.MaxConcurrency, _batchOptions.MaxConcurrency);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var document = await UploadDocumentAsync(
                    file.Stream,
                    file.FileName,
                    file.ContentType,
                    tags,
                    chunkingStrategy,
                    cancellationToken);

                return new BatchUploadItemResult
                {
                    FileName = file.FileName,
                    Succeeded = true,
                    DocumentId = document.Id,
                    ChunkCount = document.ChunkCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch upload: file '{FileName}' failed", file.FileName);
                return new BatchUploadItemResult
                {
                    FileName = file.FileName,
                    Succeeded = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = (await Task.WhenAll(tasks)).ToList();

        _logger.LogInformation(
            "Batch upload complete. Succeeded: {Ok}, Failed: {Fail}",
            results.Count(r => r.Succeeded),
            results.Count(r => !r.Succeeded));

        return results;
    }
}

// Argha - 2026-02-21 - Internal result model for batch upload, mapped to DTO at the controller layer 
/// <summary>
/// Per-file result produced by DocumentService.BatchUploadDocumentsAsync
/// </summary>
public class BatchUploadItemResult
{
    public string FileName { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public Guid? DocumentId { get; set; }
    public int? ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
}

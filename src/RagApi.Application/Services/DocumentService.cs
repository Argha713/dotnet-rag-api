using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

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
    
    // In-memory document store (replace with database in production)
    private static readonly ConcurrentDictionary<Guid, Document> _documents = new();

    public DocumentService(
        IDocumentProcessor documentProcessor,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<DocumentService> logger)
    {
        _documentProcessor = documentProcessor;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>
    /// Upload and process a document
    /// </summary>
    public async Task<Document> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading document: {FileName} ({ContentType})", fileName, contentType);

        // Validate content type
        if (!_documentProcessor.IsSupported(contentType))
        {
            throw new NotSupportedException(
                $"Content type '{contentType}' is not supported. Supported types: {string.Join(", ", _documentProcessor.SupportedContentTypes)}");
        }

        // Create document record
        var document = new Document
        {
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileStream.Length,
            Status = DocumentStatus.Processing
        };
        _documents[document.Id] = document;

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
            var chunks = _documentProcessor.ChunkText(document.Id, text);
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
            }

            // Step 4: Store chunks in vector database
            await _vectorStore.UpsertChunksAsync(chunks, cancellationToken);

            // Update document status
            document.Status = DocumentStatus.Completed;
            document.ChunkCount = chunks.Count;
            
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
            throw;
        }
    }

    /// <summary>
    /// Get document by ID
    /// </summary>
    public Task<Document?> GetDocumentAsync(Guid documentId)
    {
        _documents.TryGetValue(documentId, out var document);
        return Task.FromResult(document);
    }

    /// <summary>
    /// Get all documents
    /// </summary>
    public Task<List<Document>> GetAllDocumentsAsync()
    {
        return Task.FromResult(_documents.Values.ToList());
    }

    /// <summary>
    /// Delete a document and its chunks
    /// </summary>
    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting document {DocumentId}", documentId);

        // Remove chunks from vector store
        await _vectorStore.DeleteDocumentChunksAsync(documentId, cancellationToken);

        // Remove document record
        _documents.TryRemove(documentId, out _);
        
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

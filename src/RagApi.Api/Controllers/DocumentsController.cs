using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;

namespace RagApi.Api.Controllers;

/// <summary>
/// Controller for document management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;

    public DocumentsController(DocumentService documentService)
    {
        _documentService = documentService;
    }

    /// <summary>
    /// Upload a document for processing
    /// </summary>
    /// <param name="file">The document file (PDF, DOCX, TXT)</param>
    /// <param name="tags">Optional tags for metadata filtering (send multiple times for multiple tags)</param>
    /// <returns>The uploaded document information</returns>
    // Argha - 2026-02-19 - Added optional tags form field for metadata filtering 
    // Argha - 2026-02-20 - Added optional chunkingStrategy form field for per-upload strategy selection 
    [HttpPost]
    [RequestSizeLimit(50_000_000)] // 50MB limit
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadDocument(
        IFormFile file,
        [FromForm] List<string>? tags,
        [FromForm] string? chunkingStrategy,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        // Argha - 2026-02-20 - Validate chunkingStrategy string; 400 if unrecognized 
        ChunkingStrategy? parsedStrategy = null;
        if (!string.IsNullOrWhiteSpace(chunkingStrategy))
        {
            if (!Enum.TryParse<ChunkingStrategy>(chunkingStrategy, ignoreCase: true, out var s))
            {
                return BadRequest(new
                {
                    error = $"Invalid chunkingStrategy '{chunkingStrategy}'. Valid values: {string.Join(", ", Enum.GetNames<ChunkingStrategy>())}"
                });
            }
            parsedStrategy = s;
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.UploadDocumentAsync(
            stream,
            file.FileName,
            file.ContentType,
            tags,
            parsedStrategy,
            cancellationToken);

        var dto = MapToDto(document);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, dto);
    }

    /// <summary>
    /// Get all uploaded documents, optionally filtered by a tag
    /// </summary>
    // Argha - 2026-02-19 - Added optional tag query parameter for listing 
    [HttpGet]
    [ProducesResponseType(typeof(List<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllDocuments([FromQuery] string? tag = null, CancellationToken cancellationToken = default)
    {
        var documents = await _documentService.GetAllDocumentsAsync(tag, cancellationToken);
        var dtos = documents.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get a specific document by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        var document = await _documentService.GetDocumentAsync(id);
        if (document == null)
        {
            return NotFound(new { error = "Document not found" });
        }
        return Ok(MapToDto(document));
    }

    /// <summary>
    /// Delete a document and its chunks
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documentService.GetDocumentAsync(id);
        if (document == null)
        {
            return NotFound(new { error = "Document not found" });
        }

        await _documentService.DeleteDocumentAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Get supported file types
    /// </summary>
    [HttpGet("supported-types")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public IActionResult GetSupportedTypes()
    {
        return Ok(_documentService.GetSupportedContentTypes());
    }

    private static DocumentDto MapToDto(Domain.Entities.Document document)
    {
        return new DocumentDto
        {
            Id = document.Id,
            FileName = document.FileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            UploadedAt = document.UploadedAt,
            Status = document.Status.ToString(),
            ChunkCount = document.ChunkCount,
            ErrorMessage = document.ErrorMessage,
            // Argha - 2026-02-19 - Deserialize TagsJson for API response 
            Tags = JsonSerializer.Deserialize<List<string>>(document.TagsJson) ?? new List<string>()
        };
    }
}

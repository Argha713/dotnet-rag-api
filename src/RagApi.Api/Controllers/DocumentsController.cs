using Microsoft.AspNetCore.Mvc;
using RagApi.Api.Models;
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
    /// <returns>The uploaded document information</returns>
    [HttpPost]
    [RequestSizeLimit(50_000_000)] // 50MB limit
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadDocument(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        await using var stream = file.OpenReadStream();
        var document = await _documentService.UploadDocumentAsync(
            stream,
            file.FileName,
            file.ContentType,
            cancellationToken);

        var dto = MapToDto(document);
        return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, dto);
    }

    /// <summary>
    /// Get all uploaded documents
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllDocuments()
    {
        var documents = await _documentService.GetAllDocumentsAsync();
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
            ErrorMessage = document.ErrorMessage
        };
    }
}

using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-03-16 - #31 - Contract for persisting and retrieving DocumentImage records.
// PostgreSQL implementation in Infrastructure (#33).
public interface IImageStore
{
    // Argha - 2026-03-16 - #31 - Persists image to the store; returns the image's Id
    // (same as image.Id — callers can use the return value without keeping entity reference)
    Task<Guid> SaveAsync(DocumentImage image, CancellationToken ct = default);

    // Argha - 2026-03-16 - #31 - Returns null when no image with the given id exists
    Task<DocumentImage?> GetAsync(Guid id, CancellationToken ct = default);

    // Argha - 2026-03-17 - #37 - Streams image bytes directly from storage without server-side buffering.
    // Returns null when no image with the given id exists in the current workspace.
    // Caller must dispose the result (or its Body stream) after consuming it.
    Task<ImageStreamResult?> GetStreamAsync(Guid id, CancellationToken ct = default);

    // Argha - 2026-03-16 - #31 - Cascade delete: removes all images for a document.
    // Called by DocumentService (#36) during document deletion.
    Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default);
}

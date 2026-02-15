using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-02-15 - Repository interface for persistent document storage (replaces in-memory ConcurrentDictionary)
public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Document>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

using Microsoft.EntityFrameworkCore;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-15 - SQLite-backed document repository via EF Core
public class DocumentRepository : IDocumentRepository
{
    private readonly RagApiDbContext _dbContext;

    public DocumentRepository(RagApiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<List<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _dbContext.Documents.Update(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents.FindAsync(new object[] { id }, cancellationToken);
        if (document != null)
        {
            _dbContext.Documents.Remove(document);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

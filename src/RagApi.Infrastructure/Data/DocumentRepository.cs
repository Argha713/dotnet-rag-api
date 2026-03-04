using Microsoft.EntityFrameworkCore;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-15 - PostgreSQL-backed document repository via EF Core
// Argha - 2026-03-04 - #17 - All queries scoped to the current workspace via IWorkspaceContext
public class DocumentRepository : IDocumentRepository
{
    private readonly RagApiDbContext _dbContext;
    // Argha - 2026-03-04 - #17 - Scoped workspace context; populated by ApiKeyMiddleware each request
    private readonly IWorkspaceContext _workspaceContext;

    public DocumentRepository(RagApiDbContext dbContext, IWorkspaceContext workspaceContext)
    {
        _dbContext = dbContext;
        _workspaceContext = workspaceContext;
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-04 - #17 - Bind document to current workspace before persisting
        document.WorkspaceId = _workspaceContext.Current.Id;
        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-04 - #17 - WorkspaceId filter prevents cross-tenant document access
        return await _dbContext.Documents
            .FirstOrDefaultAsync(
                d => d.Id == id && d.WorkspaceId == _workspaceContext.Current.Id,
                cancellationToken);
    }

    public async Task<List<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Argha - 2026-03-04 - #17 - Return only documents belonging to the current workspace
        return await _dbContext.Documents
            .Where(d => d.WorkspaceId == _workspaceContext.Current.Id)
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
        // Argha - 2026-03-04 - #17 - Workspace filter ensures we only delete documents we own
        var document = await _dbContext.Documents
            .FirstOrDefaultAsync(
                d => d.Id == id && d.WorkspaceId == _workspaceContext.Current.Id,
                cancellationToken);
        if (document != null)
        {
            _dbContext.Documents.Remove(document);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

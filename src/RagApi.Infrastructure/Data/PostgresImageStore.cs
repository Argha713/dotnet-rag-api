using Microsoft.EntityFrameworkCore;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-03-16 - #33 - PostgreSQL-backed image store; persists raw image bytes
// as bytea rows in DocumentImages; all queries workspace-scoped via IWorkspaceContext
public class PostgresImageStore : IImageStore
{
    private readonly RagApiDbContext _dbContext;
    private readonly IWorkspaceContext _workspaceContext;

    public PostgresImageStore(RagApiDbContext dbContext, IWorkspaceContext workspaceContext)
    {
        _dbContext = dbContext;
        _workspaceContext = workspaceContext;
    }

    public async Task<Guid> SaveAsync(DocumentImage image, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - Bind to current workspace before persisting;
        // overwrites any caller-supplied WorkspaceId to prevent cross-tenant writes
        image.WorkspaceId = _workspaceContext.Current.Id;
        _dbContext.DocumentImages.Add(image);
        await _dbContext.SaveChangesAsync(ct);
        return image.Id;
    }

    public async Task<DocumentImage?> GetAsync(Guid id, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - WorkspaceId filter prevents cross-tenant image access
        return await _dbContext.DocumentImages
            .FirstOrDefaultAsync(
                i => i.Id == id && i.WorkspaceId == _workspaceContext.Current.Id,
                ct);
    }

    public async Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        // Argha - 2026-03-16 - #33 - Workspace filter added for defence-in-depth:
        // ensures we only delete images we own even if called with an arbitrary documentId
        var images = await _dbContext.DocumentImages
            .Where(i => i.DocumentId == documentId && i.WorkspaceId == _workspaceContext.Current.Id)
            .ToListAsync(ct);

        if (images.Count > 0)
        {
            _dbContext.DocumentImages.RemoveRange(images);
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}

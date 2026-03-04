using Microsoft.EntityFrameworkCore;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-03-04 - #17 - EF Core implementation of IWorkspaceRepository; not workspace-scoped (this IS the workspace table)
public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly RagApiDbContext _dbContext;

    public WorkspaceRepository(RagApiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Workspace> CreateAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        _dbContext.Workspaces.Add(workspace);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Workspaces.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<List<Workspace>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Workspaces
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Workspace?> GetByApiKeyHashAsync(string hashedKey, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Workspaces
            .FirstOrDefaultAsync(w => w.HashedApiKey == hashedKey, cancellationToken);
    }

    public async Task UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        _dbContext.Workspaces.Update(workspace);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workspace = await _dbContext.Workspaces.FindAsync(new object[] { id }, cancellationToken);
        if (workspace != null)
        {
            _dbContext.Workspaces.Remove(workspace);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-03-04 - #17 - Repository interface for workspace CRUD; not workspace-scoped (this IS the workspace table)
public interface IWorkspaceRepository
{
    Task<Workspace> CreateAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Workspace>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Workspace?> GetByApiKeyHashAsync(string hashedKey, CancellationToken cancellationToken = default);
    Task UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Services;

// Argha - 2026-03-04 - #17 - Scoped per-request workspace carrier; populated by ApiKeyMiddleware before controllers run
public class WorkspaceContext : IWorkspaceContext
{
    public Workspace Current { get; set; } = null!;
    public bool IsResolved => Current is not null;
}

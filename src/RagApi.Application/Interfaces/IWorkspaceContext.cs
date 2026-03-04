using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

// Argha - 2026-03-04 - #17 - Scoped carrier populated by ApiKeyMiddleware before controllers run;
// every authenticated request resolves to exactly one workspace
public interface IWorkspaceContext
{
    Workspace Current { get; set; }
    bool IsResolved { get; }
}

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Application.Services;

// Argha - 2026-03-04 - #17 - Business logic for workspace lifecycle; called by WorkspacesController
public class WorkspaceService
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IWorkspaceRepository workspaceRepository,
        IVectorStore vectorStore,
        ILogger<WorkspaceService> logger)
    {
        _workspaceRepository = workspaceRepository;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    // Argha - 2026-03-04 - #17 - Returns (workspace, plaintext key); caller's only chance to read the key
    public async Task<(Workspace Workspace, string PlainTextKey)> CreateWorkspaceAsync(
        string name, CancellationToken cancellationToken = default)
    {
        // Generate 32 cryptographically random bytes → hex → SHA-256 hash stored in DB
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plainTextKey = Convert.ToHexString(keyBytes).ToLowerInvariant();
        var hashedKey = ComputeSha256(plainTextKey);

        var workspace = new Workspace
        {
            Name = name,
            HashedApiKey = hashedKey,
            CreatedAt = DateTime.UtcNow
        };

        // Argha - 2026-03-04 - #17 - CollectionName derived from Id so it is unique per workspace
        workspace.CollectionName = $"ws_{workspace.Id:N}";

        await _workspaceRepository.CreateAsync(workspace, cancellationToken);

        _logger.LogInformation(
            "Creating Qdrant collection for workspace {WorkspaceId}: {Collection}",
            workspace.Id, workspace.CollectionName);

        await _vectorStore.EnsureCollectionAsync(workspace.CollectionName, cancellationToken);

        return (workspace, plainTextKey);
    }

    public async Task<Workspace?> GetWorkspaceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _workspaceRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteWorkspaceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Workspace '{id}' not found.");

        // Argha - 2026-03-04 - #17 - Delete Qdrant collection first; if already gone, log and continue
        try
        {
            await _vectorStore.DeleteCollectionAsync(workspace.CollectionName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not delete Qdrant collection '{Collection}' for workspace {WorkspaceId} — continuing with DB delete",
                workspace.CollectionName, id);
        }

        // Argha - 2026-03-04 - #17 - EF Core CASCADE handles Documents and ConversationSessions automatically
        await _workspaceRepository.DeleteAsync(id, cancellationToken);
        _logger.LogInformation("Workspace {WorkspaceId} deleted", id);
    }

    // Argha - 2026-03-04 - #17 - SHA-256 hex of the plaintext key for DB lookup
    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

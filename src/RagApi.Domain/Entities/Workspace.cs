namespace RagApi.Domain.Entities;

// Argha - 2026-03-04 - #17 - Workspace entity for multi-tenancy; each workspace has an isolated Qdrant
// collection and its own DB rows for Documents and ConversationSessions
public class Workspace
{
    // Argha - 2026-03-04 - #17 - Fixed Id used to seed the default workspace; maps to the legacy "documents" collection
    public static readonly Guid DefaultWorkspaceId = new Guid("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    // Argha - 2026-03-04 - #17 - SHA-256 hex of the plaintext API key; plaintext is never stored
    public string HashedApiKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Argha - 2026-03-04 - #17 - "documents" for default workspace; "ws_{Id:N}" for new workspaces
    public string CollectionName { get; set; } = string.Empty;

    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<ConversationSession> Sessions { get; set; } = new List<ConversationSession>();
}

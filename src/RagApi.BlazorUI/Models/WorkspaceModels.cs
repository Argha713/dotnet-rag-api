namespace RagApi.BlazorUI.Models;

// Argha - 2026-03-04 - #17 - Workspace UI models: mirrors API DTOs + local storage structure

public record WorkspaceDto(Guid Id, string Name, DateTime CreatedAt, string CollectionName);

public record WorkspaceCreatedDto(Guid Id, string Name, DateTime CreatedAt, string CollectionName, string ApiKey);

public class StoredWorkspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

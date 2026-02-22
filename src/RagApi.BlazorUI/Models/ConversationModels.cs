namespace RagApi.BlazorUI.Models;

// Argha - 2026-02-21 - Conversation session DTOs mirroring RagApi.Api.Models session types 

public class CreateSessionResponse
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SessionDto
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string? Title { get; set; }
    public List<SessionMessageDto> Messages { get; set; } = new();
}

public class SessionMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

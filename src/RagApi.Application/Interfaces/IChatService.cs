using RagApi.Domain.Entities;

namespace RagApi.Application.Interfaces;

/// <summary>
/// Interface for chat/completion functionality
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Generate a chat completion response
    /// </summary>
    Task<string> GenerateResponseAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generate a chat completion with streaming
    /// </summary>
    IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string systemPrompt,
        List<ChatMessage> messages,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the name of the model being used
    /// </summary>
    string ModelName { get; }
}

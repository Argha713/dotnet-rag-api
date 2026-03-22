namespace RagApi.Application.Interfaces;

// Argha - 2026-03-22 - #81 - Audio transcription abstraction; real impl uses Whisper, null impl rejects audio
public interface IAudioTranscriptionService
{
    /// <summary>
    /// Whether this provider supports audio transcription.
    /// Returns false for Ollama (no Whisper endpoint available).
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Transcribe audio from a stream to text.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when IsSupported is false.</exception>
    Task<string> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken cancellationToken = default);
}

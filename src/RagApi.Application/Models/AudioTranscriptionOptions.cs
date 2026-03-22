namespace RagApi.Application.Models;

// Argha - 2026-03-22 - #81 - Configuration for the Whisper transcription service
public class AudioTranscriptionOptions
{
    public const string SectionName = "AudioTranscription";

    // Argha - 2026-03-22 - #81 - Whisper model identifier (only used by OpenAI/Azure providers)
    public string WhisperModel { get; set; } = "whisper-1";
}

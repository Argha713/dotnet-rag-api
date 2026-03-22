// Argha - 2026-03-22 - #81 - Null-object registered when AI:Provider = Ollama (no Whisper support)
using RagApi.Application.Interfaces;

namespace RagApi.Infrastructure.AI;

// Argha - 2026-03-22 - #81 - IsSupported=false lets DocumentService skip transcription without
// entering this class; TranscribeAsync throws to catch misconfigured callers
internal sealed class NullAudioTranscriptionService : IAudioTranscriptionService
{
    public bool IsSupported => false;

    public Task<string> TranscribeAsync(
        Stream audioStream,
        string mimeType,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Audio transcription requires an OpenAI or AzureOpenAI provider. Ollama does not support Whisper.");
}

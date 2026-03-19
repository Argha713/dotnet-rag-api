using RagApi.Application.Interfaces;

namespace RagApi.Infrastructure.AI;

/// <summary>
/// No-op OCR service registered when OCR is disabled or not configured.
/// </summary>
internal sealed class NullOcrService : IOcrService
{
    public bool IsEnabled => false;

    public Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}

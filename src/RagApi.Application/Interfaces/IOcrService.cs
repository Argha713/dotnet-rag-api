namespace RagApi.Application.Interfaces;

/// <summary>
/// OCR service for extracting text from image bytes.
/// Used as a fallback when PDF text extraction yields no text (scanned documents).
/// </summary>
public interface IOcrService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Runs character recognition on a single image and returns the extracted text.
    /// Returns an empty string when recognition yields no output or the service is disabled.
    /// </summary>
    Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

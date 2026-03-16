namespace RagApi.Application.Interfaces;

// Argha - 2026-03-16 - #31 - Contract for AI vision description of images extracted from documents.
// GPT-4o implementation in Infrastructure (#32); false IsEnabled guards callers when no vision model configured.
public interface IVisionService
{
    // Argha - 2026-03-16 - #31 - imageBytes: raw image data; mimeType: e.g. "image/png";
    // context: optional hint to the model (e.g. document title or surrounding text)
    Task<string> DescribeImageAsync(
        byte[] imageBytes,
        string mimeType,
        string? context = null,
        CancellationToken ct = default);

    // Argha - 2026-03-16 - #31 - False when no vision-capable model is configured;
    // DocumentService (#36) skips vision processing entirely when false
    bool IsEnabled { get; }
}

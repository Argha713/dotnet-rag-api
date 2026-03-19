using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using Tesseract;

namespace RagApi.Infrastructure.AI;

/// <summary>
/// OCR service backed by Tesseract.NET.
/// Registered as Singleton — TesseractEngine is thread-safe for read operations.
/// Requires tessdata files and native Tesseract libraries (see Dockerfile).
/// </summary>
internal sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly ILogger<TesseractOcrService> _logger;

    public bool IsEnabled => true;

    public TesseractOcrService(IOptions<OcrOptions> options, ILogger<TesseractOcrService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _engine = new TesseractEngine(opts.TessDataPath, opts.Language, EngineMode.Default);
    }

    public Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(pix);
            var text = page.GetText() ?? string.Empty;
            return Task.FromResult(text.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract OCR failed for image ({Bytes} bytes); skipping", imageBytes.Length);
            return Task.FromResult(string.Empty);
        }
    }

    public void Dispose() => _engine.Dispose();
}

using AngleSharp;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace RagApi.Infrastructure.DocumentProcessing;

// Argha - 2026-03-22 - #74 - Fetches content from a URL: HTML pages are stripped to plain text; YouTube URLs
// use YoutubeExplode captions with Whisper audio transcription as fallback
public class UrlScraper : IUrlScraper
{
    private readonly HttpClient _httpClient;
    private readonly IAudioTranscriptionService _audioService;
    private readonly UrlIngestionOptions _options;
    private readonly ILogger<UrlScraper> _logger;

    public UrlScraper(
        HttpClient httpClient,
        IAudioTranscriptionService audioService,
        IOptions<UrlIngestionOptions> options,
        ILogger<UrlScraper> logger)
    {
        _httpClient = httpClient;
        _audioService = audioService;
        _options = options.Value;
        _logger = logger;

        // Argha - 2026-03-22 - #74 - Apply user-agent and timeout from config
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    // Argha - 2026-03-22 - #74 - Scrape text content from a URL; auto-detects YouTube for caption extraction
    public async Task<(string Text, string FileName)> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));

        // Argha - 2026-03-22 - #74 - Only http and https schemes are permitted
        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException($"Unsupported URL scheme '{uri.Scheme}'. Only http and https are allowed.", nameof(url));

        // Argha - 2026-03-22 - #74 - Detect YouTube URLs and route to caption/audio pipeline
        if (IsYouTubeUrl(uri))
        {
            _logger.LogInformation("URL is YouTube; attempting caption extraction for {Url}", url);
            return await ScrapeYouTubeAsync(url, cancellationToken);
        }

        return await ScrapeHtmlAsync(url, cancellationToken);
    }

    // Argha - 2026-03-22 - #74 - Fetches and strips HTML content to plain text via AngleSharp
    private async Task<(string Text, string FileName)> ScrapeHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Argha - 2026-03-22 - #74 - Guard against unexpectedly large responses
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > _options.MaxContentLengthBytes)
            throw new InvalidOperationException(
                $"Response content length {contentLength.Value} exceeds limit of {_options.MaxContentLengthBytes} bytes.");

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        if (html.Length > _options.MaxContentLengthBytes)
            throw new InvalidOperationException(
                $"Response content length {html.Length} exceeds limit of {_options.MaxContentLengthBytes} bytes.");

        var text = await StripHtmlAsync(html, cancellationToken);

        // Argha - 2026-03-22 - #74 - Use the URL path as a meaningful filename fallback
        var path = new Uri(url).AbsolutePath.TrimEnd('/');
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "scraped-page.txt";
        if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) fileName += ".txt";

        return (text, fileName);
    }

    // Argha - 2026-03-22 - #74 - Extract plain text from HTML using AngleSharp; strips scripts, styles, nav
    private static async Task<string> StripHtmlAsync(string html, CancellationToken cancellationToken)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var parser = context.GetService<IHtmlParser>()!;
        var document = await parser.ParseDocumentAsync(html, cancellationToken);

        // Argha - 2026-03-22 - #74 - Remove non-content elements before text extraction
        foreach (var element in document.QuerySelectorAll("script, style, nav, header, footer, aside, noscript"))
            element.Remove();

        return document.Body?.TextContent?.Trim() ?? string.Empty;
    }

    // Argha - 2026-03-22 - #74 - YouTube pipeline: captions first, Whisper audio fallback
    private async Task<(string Text, string FileName)> ScrapeYouTubeAsync(string url, CancellationToken cancellationToken)
    {
        var youtube = new YoutubeClient();

        // Argha - 2026-03-22 - #74 - Try auto-generated + manually uploaded captions
        try
        {
            var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(url, cancellationToken);
            // Argha - 2026-03-22 - #74 - TryGetByLanguage returns ClosedCaptionTrackInfo? (null if not found)
            var track = trackManifest.TryGetByLanguage("en") ?? trackManifest.Tracks.FirstOrDefault();

            if (track != null)
            {
                _logger.LogInformation("YouTube captions found ({Language}); fetching for {Url}", track.Language, url);
                var captions = await youtube.Videos.ClosedCaptions.GetAsync(track, cancellationToken);
                var text = string.Join(" ", captions.Captions.Select(c => c.Text));
                var video = await youtube.Videos.GetAsync(url, cancellationToken);
                var fileName = $"{SanitizeFileName(video.Title)}.txt";
                return (text, fileName);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube caption extraction failed for {Url}; trying Whisper fallback", url);
        }

        // Argha - 2026-03-22 - #74 - Caption fetch failed; fall back to Whisper audio transcription
        if (!_audioService.IsSupported)
        {
            throw new NotSupportedException(
                "YouTube video has no captions and audio transcription is not available with the current AI provider. Use OpenAI or AzureOpenAI.");
        }

        _logger.LogInformation("Falling back to Whisper audio transcription for {Url}", url);
        return await TranscribeYouTubeAudioAsync(url, youtube, cancellationToken);
    }

    // Argha - 2026-03-22 - #74 - Download the lowest-bitrate audio stream and transcribe via Whisper
    private async Task<(string Text, string FileName)> TranscribeYouTubeAudioAsync(
        string url, YoutubeClient youtube, CancellationToken cancellationToken)
    {
        var video = await youtube.Videos.GetAsync(url, cancellationToken);
        var streamManifest = await youtube.Videos.Streams.GetManifestAsync(url, cancellationToken);

        // Argha - 2026-03-22 - #74 - Pick the smallest audio-only stream to minimise download size
        var audioStreamInfo = streamManifest.GetAudioOnlyStreams()
            .OrderBy(s => s.Bitrate.BitsPerSecond)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No audio-only stream found for YouTube video.");

        using var audioStream = new MemoryStream();
        // Argha - 2026-03-22 - #74 - CopyToAsync signature: (IStreamInfo, Stream, IProgress<double>?, CancellationToken)
        await youtube.Videos.Streams.CopyToAsync(audioStreamInfo, audioStream, null, cancellationToken);
        audioStream.Position = 0;

        var mimeType = audioStreamInfo.Container.Name.ToLowerInvariant() switch
        {
            "mp4" or "m4a" => "audio/mp4",
            "webm" => "audio/webm",
            _ => "audio/mpeg"
        };

        var text = await _audioService.TranscribeAsync(audioStream, mimeType, cancellationToken);
        var fileName = $"{SanitizeFileName(video.Title)}.txt";
        return (text, fileName);
    }

    // Argha - 2026-03-22 - #74 - Detect YouTube domains (including youtu.be short links)
    private static bool IsYouTubeUrl(Uri uri) =>
        uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);

    // Argha - 2026-03-22 - #74 - Strip filesystem-unsafe characters from video titles for use as filenames
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}

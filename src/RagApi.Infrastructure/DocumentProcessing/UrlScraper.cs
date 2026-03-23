using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using YoutubeExplode;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos.Streams;

namespace RagApi.Infrastructure.DocumentProcessing;

// Argha - 2026-03-22 - #74 - Fetches content from a URL: HTML pages are stripped to plain text; YouTube URLs
// use direct page-scraping for captions (bypasses po_token blocking) with Whisper audio transcription as fallback
// Argha - 2026-03-23 - #102 - Replaced YoutubeExplode caption extraction with direct watch-page HTML scraping
// to bypass YouTube's server-side po_token requirement that blocks all non-browser player API requests
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

        // Argha - 2026-03-23 - #102 - Do NOT set User-Agent as a default header; set it per-request so
        // FetchYouTubeCaptionsFromPageAsync can send a clean browser User-Agent without duplication
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
        // Argha - 2026-03-23 - #102 - Set User-Agent per-request (not as default header) so YouTube requests can use a different UA
        using var htmlRequest = new HttpRequestMessage(HttpMethod.Get, url);
        htmlRequest.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        using var response = await _httpClient.SendAsync(htmlRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    // Argha - 2026-03-23 - #102 - YouTube pipeline: direct page-scraping first (bypasses po_token), then Whisper fallback
    // YoutubeExplode's player API calls are blocked from Azure server IPs since YouTube enforced po_token in 2024
    private async Task<(string Text, string FileName)> ScrapeYouTubeAsync(string url, CancellationToken cancellationToken)
    {
        // Argha - 2026-03-23 - #102 - Primary: fetch watch page HTML and extract ytInitialPlayerResponse captions
        try
        {
            var result = await FetchYouTubeCaptionsFromPageAsync(url, cancellationToken);
            if (result.HasValue)
            {
                _logger.LogInformation("YouTube captions extracted via page scraping for {Url}", url);
                return result.Value;
            }
            _logger.LogInformation("No captions found via page scraping for {Url}; trying Whisper fallback", url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YouTube page-scraping caption extraction failed for {Url}; trying Whisper fallback", url);
        }

        // Argha - 2026-03-23 - #102 - Whisper fallback: download audio via YoutubeExplode
        if (!_audioService.IsSupported)
        {
            throw new NotSupportedException(
                "YouTube video has no captions and audio transcription is not available with the current AI provider. Use OpenAI or AzureOpenAI.");
        }

        _logger.LogInformation("Falling back to Whisper audio transcription for {Url}", url);

        try
        {
            var youtube = new YoutubeClient();
            return await TranscribeYouTubeAudioAsync(url, youtube, cancellationToken);
        }
        catch (VideoUnavailableException ex)
        {
            // Argha - 2026-03-23 - #102 - YoutubeExplode throws this when server-side po_token check blocks the request
            _logger.LogWarning(ex, "YouTube video unavailable (likely server-side blocking) for {Url}", url);
            throw new InvalidOperationException(
                "This video is not accessible from the server. It may be age-restricted, geo-blocked, or require sign-in. " +
                "Try a different video or upload a transcript file directly.");
        }
        catch (YoutubeExplodeException ex)
        {
            _logger.LogWarning(ex, "YoutubeExplode error during audio extraction for {Url}", url);
            throw new InvalidOperationException(
                $"Unable to process YouTube video: {ex.Message}. " +
                "The video may be private, deleted, or restricted.");
        }
    }

    // Argha - 2026-03-23 - #102 - Fetch caption track URLs by parsing ytInitialPlayerResponse from the watch page HTML.
    // This avoids hitting the InnerTube player API (/youtubei/v1/player) that requires po_token from browser JS.
    // Returns null when the page loads successfully but the video has no caption tracks.
    private async Task<(string Text, string FileName)?> FetchYouTubeCaptionsFromPageAsync(
        string url, CancellationToken cancellationToken)
    {
        var videoId = ExtractYouTubeVideoId(url);
        var watchUrl = $"https://www.youtube.com/watch?v={videoId}";

        // Argha - 2026-03-23 - #102 - Browser-like headers are required; YouTube returns a consent/bot page without them.
        // SOCS=CAI bypasses the GDPR cookie consent gate that YouTube shows to non-browser requests from server IPs.
        // User-Agent is set ONLY here (not in HttpClient.DefaultRequestHeaders) to avoid sending a double User-Agent header.
        using var request = new HttpRequestMessage(HttpMethod.Get, watchUrl);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI; CONSENT=YES+cb");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // Argha - 2026-03-23 - #102 - Extract ytInitialPlayerResponse JSON blob embedded in the page
        var playerResponseJson = ExtractYtInitialPlayerResponse(html);
        if (playerResponseJson == null)
        {
            _logger.LogDebug("ytInitialPlayerResponse not found in watch page for {Url}", url);
            return null;
        }

        using var doc = JsonDocument.Parse(playerResponseJson);
        var root = doc.RootElement;

        // Argha - 2026-03-23 - #102 - Navigate: videoDetails.title for filename
        var title = root.TryGetProperty("videoDetails", out var vd) && vd.TryGetProperty("title", out var t)
            ? t.GetString() ?? videoId
            : videoId;

        // Argha - 2026-03-23 - #102 - Navigate: captions.playerCaptionsTracklistRenderer.captionTracks[]
        if (!root.TryGetProperty("captions", out var captions) ||
            !captions.TryGetProperty("playerCaptionsTracklistRenderer", out var tracklistRenderer) ||
            !tracklistRenderer.TryGetProperty("captionTracks", out var captionTracks) ||
            captionTracks.GetArrayLength() == 0)
        {
            _logger.LogDebug("No captionTracks found in ytInitialPlayerResponse for {Url}", url);
            return null;
        }

        // Argha - 2026-03-23 - #102 - Prefer English; fall back to first available track
        string? captionBaseUrl = null;
        foreach (var track in captionTracks.EnumerateArray())
        {
            if (track.TryGetProperty("languageCode", out var lang) && lang.GetString() == "en")
            {
                captionBaseUrl = track.TryGetProperty("baseUrl", out var bu) ? bu.GetString() : null;
                break;
            }
        }
        if (captionBaseUrl == null)
        {
            var firstTrack = captionTracks.EnumerateArray().First();
            captionBaseUrl = firstTrack.TryGetProperty("baseUrl", out var bu) ? bu.GetString() : null;
        }

        if (string.IsNullOrEmpty(captionBaseUrl))
            return null;

        // Argha - 2026-03-23 - #102 - Download caption XML and parse <text> elements
        var captionXml = await _httpClient.GetStringAsync(captionBaseUrl, cancellationToken);
        var text = ParseCaptionXml(captionXml);

        if (string.IsNullOrWhiteSpace(text))
            return null;

        var fileName = $"{SanitizeFileName(title)}.txt";
        return (text, fileName);
    }

    // Argha - 2026-03-23 - #102 - Locate the ytInitialPlayerResponse = {...}; assignment in the watch page script
    // and extract the JSON value using a balanced-brace walk (avoids fragile substring/regex on deeply nested JSON)
    private static string? ExtractYtInitialPlayerResponse(string html)
    {
        const string marker = "var ytInitialPlayerResponse = ";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;

        if (start >= html.Length || html[start] != '{') return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < html.Length; i++)
        {
            var ch = html[i];
            if (escape) { escape = false; continue; }
            if (ch == '\\' && inString) { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return html.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    // Argha - 2026-03-23 - #102 - Parse YouTube caption XML: <text start="..." dur="...">...</text>
    // Uses regex because the XML can contain HTML entities and the schema is simple and stable
    private static string ParseCaptionXml(string xml)
    {
        var matches = Regex.Matches(xml, @"<text[^>]*>(.*?)</text>", RegexOptions.Singleline);
        var parts = matches.Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Trim())
                           .Where(s => !string.IsNullOrEmpty(s));
        return string.Join(" ", parts);
    }

    // Argha - 2026-03-23 - #102 - Extract video ID from youtube.com/watch?v= or youtu.be/ URLs
    private static string ExtractYouTubeVideoId(string url)
    {
        var uri = new Uri(url);

        // youtu.be/VIDEO_ID
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.TrimStart('/').Split('?')[0];

        // youtube.com/watch?v=VIDEO_ID or youtube.com/shorts/VIDEO_ID
        if (uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Split('/')[2];

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["v"] ?? throw new ArgumentException($"Cannot extract video ID from URL: {url}");
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

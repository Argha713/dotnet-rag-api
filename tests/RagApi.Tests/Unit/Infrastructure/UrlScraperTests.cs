// Argha - 2026-03-22 - #83 - Unit tests for UrlScraper: URL validation, HTML stripping, size guard
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Infrastructure.DocumentProcessing;

namespace RagApi.Tests.Unit.Infrastructure;

public class UrlScraperTests
{
    // Argha - 2026-03-22 - #83 - Build a test HttpClient backed by a mock handler that returns the given response
    private static HttpClient CreateHttpClient(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return new HttpClient(handler.Object);
    }

    private static UrlScraper CreateScraper(HttpClient httpClient, UrlIngestionOptions? options = null)
    {
        var opts = Options.Create(options ?? new UrlIngestionOptions());
        return new UrlScraper(
            httpClient,
            Mock.Of<IAudioTranscriptionService>(),
            opts,
            Mock.Of<ILogger<UrlScraper>>());
    }

    [Fact]
    public async Task ScrapeAsync_NonHttpScheme_ThrowsArgumentException()
    {
        // Argha - 2026-03-22 - #83 - ftp:// scheme must be rejected with ArgumentException
        var scraper = CreateScraper(new HttpClient());

        var act = () => scraper.ScrapeAsync("ftp://example.com");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ftp*");
    }

    [Fact]
    public async Task ScrapeAsync_InvalidUrl_ThrowsArgumentException()
    {
        // Argha - 2026-03-22 - #83 - A string that is not a valid URI must throw ArgumentException
        var scraper = CreateScraper(new HttpClient());

        var act = () => scraper.ScrapeAsync("not-a-url");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ScrapeAsync_HtmlContent_StripsTagsAndReturnsText()
    {
        // Argha - 2026-03-22 - #83 - Simple HTML must be stripped to plain text; body text must be present
        var html = "<html><body><p>Hello world</p></body></html>";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
        };

        var scraper = CreateScraper(CreateHttpClient(response));
        var (text, _) = await scraper.ScrapeAsync("https://example.com/page");

        text.Should().Contain("Hello world");
        // Argha - 2026-03-22 - #83 - HTML tags must not appear in the output
        text.Should().NotContain("<p>");
        text.Should().NotContain("<html>");
    }

    [Fact]
    public async Task ScrapeAsync_HtmlContent_StripsScriptTags()
    {
        // Argha - 2026-03-22 - #83 - Script content must be stripped; only visible text must remain
        var html = "<html><body><script>alert('x')</script><p>content</p></body></html>";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
        };

        var scraper = CreateScraper(CreateHttpClient(response));
        var (text, _) = await scraper.ScrapeAsync("https://example.com/page");

        text.Should().NotContain("alert");
        text.Should().Contain("content");
    }

    [Fact]
    public async Task ScrapeAsync_ContentTooLarge_ThrowsInvalidOperationException()
    {
        // Argha - 2026-03-22 - #83 - Response body exceeding MaxContentLengthBytes must throw InvalidOperationException
        var bigContent = new string('X', 200);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(bigContent, System.Text.Encoding.UTF8, "text/html")
        };

        var opts = new UrlIngestionOptions { MaxContentLengthBytes = 10, TimeoutSeconds = 30 };
        var scraper = CreateScraper(CreateHttpClient(response), opts);

        var act = () => scraper.ScrapeAsync("https://example.com/page");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*exceeds limit*");
    }

    [Fact]
    public async Task ScrapeAsync_ValidHtmlUrl_ReturnsFileNameWithTxtExtension()
    {
        // Argha - 2026-03-22 - #83 - The returned fileName must end with .txt
        var html = "<html><body><p>Some content</p></body></html>";
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
        };

        var scraper = CreateScraper(CreateHttpClient(response));
        var (_, fileName) = await scraper.ScrapeAsync("https://example.com/about");

        fileName.Should().EndWith(".txt");
    }
}

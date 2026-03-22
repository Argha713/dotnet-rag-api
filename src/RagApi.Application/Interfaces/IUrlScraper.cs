namespace RagApi.Application.Interfaces;

// Argha - 2026-03-22 - #74 - Abstraction for URL content scraping; implemented by UrlScraper in Infrastructure
public interface IUrlScraper
{
    Task<(string Text, string FileName)> ScrapeAsync(string url, CancellationToken cancellationToken = default);
}

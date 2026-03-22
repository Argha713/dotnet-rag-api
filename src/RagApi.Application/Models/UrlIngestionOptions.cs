namespace RagApi.Application.Models;

// Argha - 2026-03-22 - #74 - Configuration for the URL ingestion pipeline (HTTP fetch limits and UA string)
public class UrlIngestionOptions
{
    public const string SectionName = "UrlIngestion";

    // Argha - 2026-03-22 - #74 - Max allowed response size before aborting fetch (default: 5 MB)
    public int MaxContentLengthBytes { get; set; } = 5 * 1024 * 1024;

    // Argha - 2026-03-22 - #74 - HTTP request timeout in seconds
    public int TimeoutSeconds { get; set; } = 30;

    // Argha - 2026-03-22 - #74 - User-Agent sent on all fetch requests
    public string UserAgent { get; set; } = "RagApi/1.0";
}

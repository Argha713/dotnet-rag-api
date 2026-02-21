// Argha - 2026-02-21 - Configuration for batch document upload (Phase 5.2)
namespace RagApi.Application.Models;

/// <summary>
/// Configuration options for the batch document upload endpoint.
/// </summary>
public class BatchUploadOptions
{
    public const string SectionName = "BatchUpload";

    /// <summary>
    /// Maximum number of documents processed concurrently within a single batch request.
    /// Higher values increase throughput but put more load on the embedding service.
    /// </summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Maximum number of files allowed in a single batch upload request.
    /// </summary>
    public int MaxFilesPerBatch { get; set; } = 20;
}

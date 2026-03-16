namespace RagApi.Domain.Entities;

// Argha - 2026-03-16 - #30 - DocumentImage entity: stores extracted images from PDFs/DOCX
// and their AI-generated descriptions for multimodal RAG (Phase 14)
public class DocumentImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Argha - 2026-03-16 - #30 - FK to the parent document
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }

    // Argha - 2026-03-16 - #30 - Direct FK to Workspace for efficient workspace-scoped
    // queries without joining through Documents (pattern matches ConversationSession)
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    // Argha - 2026-03-16 - #30 - 1-based page number from source PDF; null for
    // non-paged sources (e.g., DOCX embedded images)
    public int? PageNumber { get; set; }

    // Argha - 2026-03-16 - #30 - MIME type (e.g., "image/png") — used by GET /api/images/{id}
    // (#37) to set the Content-Type response header
    public string ContentType { get; set; } = string.Empty;

    // Argha - 2026-03-16 - #30 - Raw image bytes persisted as PostgreSQL bytea;
    // populated by PostgresImageStore (#33) during document ingestion
    public byte[] Data { get; set; } = Array.Empty<byte>();

    // Argha - 2026-03-16 - #30 - AI-generated description from OpenAIVisionService (#32);
    // null until vision analysis completes
    public string? AiDescription { get; set; }

    // Argha - 2026-03-16 - #30 - UTC timestamp when image record was created
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

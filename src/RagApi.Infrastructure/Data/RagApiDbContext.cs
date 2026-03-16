using Microsoft.EntityFrameworkCore;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-15 - EF Core DbContext for persistent storage
// Argha - 2026-03-04 - #17 - Added Workspaces table; Documents and ConversationSessions now FK to Workspaces
public class RagApiDbContext : DbContext
{
    public RagApiDbContext(DbContextOptions<RagApiDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();

    // Argha - 2026-02-19 - Conversation sessions table
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();

    // Argha - 2026-03-04 - #17 - Workspace table for multi-tenancy
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    // Argha - 2026-03-16 - #30 - DocumentImages table for Phase 14 multimodal RAG
    public DbSet<DocumentImage> DocumentImages => Set<DocumentImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Argha - 2026-03-04 - #17 - Workspace model configuration
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(w => w.Id);

            entity.Property(w => w.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(w => w.HashedApiKey)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(w => w.CollectionName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(w => w.CreatedAt)
                .IsRequired();

            entity.HasIndex(w => w.HashedApiKey).IsUnique();
            entity.HasIndex(w => w.CreatedAt);
        });

        // Argha - 2026-02-19 - ConversationSession model configuration
        modelBuilder.Entity<ConversationSession>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Title)
                .HasMaxLength(200);

            entity.Property(s => s.MessagesJson)
                .IsRequired();

            entity.HasIndex(s => s.LastMessageAt);

            // Argha - 2026-03-04 - #17 - FK to Workspaces with CASCADE delete
            entity.Property(s => s.WorkspaceId).IsRequired();
            entity.HasOne(s => s.Workspace)
                .WithMany(w => w.Sessions)
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => new { s.WorkspaceId, s.LastMessageAt });
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);

            entity.Property(d => d.FileName)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(d => d.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(d => d.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(d => d.ErrorMessage)
                .HasMaxLength(2000);

            // Argha - 2026-02-19 - Tags stored as JSON array string
            entity.Property(d => d.TagsJson)
                .IsRequired()
                .HasDefaultValue("[]");

            entity.HasIndex(d => d.UploadedAt);

            // Argha - 2026-03-04 - #17 - FK to Workspaces with CASCADE delete
            entity.Property(d => d.WorkspaceId).IsRequired();
            entity.HasOne(d => d.Workspace)
                .WithMany(w => w.Documents)
                .HasForeignKey(d => d.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(d => new { d.WorkspaceId, d.UploadedAt });
        });

        // Argha - 2026-03-16 - #30 - DocumentImage model configuration
        modelBuilder.Entity<DocumentImage>(entity =>
        {
            entity.HasKey(i => i.Id);

            entity.Property(i => i.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            // Argha - 2026-03-16 - #30 - GPT-4o descriptions are typically 100-600 words (~3600 chars max)
            entity.Property(i => i.AiDescription)
                .HasMaxLength(4000);

            entity.Property(i => i.Data)
                .IsRequired();

            entity.Property(i => i.CreatedAt)
                .IsRequired();

            // Argha - 2026-03-16 - #30 - FK to Documents with CASCADE delete;
            // deleting a document removes all its extracted images
            entity.Property(i => i.DocumentId).IsRequired();
            entity.HasOne(i => i.Document)
                .WithMany(d => d.Images)
                .HasForeignKey(i => i.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Argha - 2026-03-16 - #30 - Direct FK to Workspaces with CASCADE delete;
            // enables workspace-scoped queries on DocumentImages without joining through Documents
            entity.Property(i => i.WorkspaceId).IsRequired();
            entity.HasOne(i => i.Workspace)
                .WithMany()
                .HasForeignKey(i => i.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Argha - 2026-03-16 - #30 - Primary access pattern: list all images for a document
            // ordered by page (used by PostgresImageStore #33)
            entity.HasIndex(i => new { i.DocumentId, i.PageNumber });

            // Argha - 2026-03-16 - #30 - Workspace-scoped single-record fetch for GET /api/images/{id} (#37)
            entity.HasIndex(i => new { i.WorkspaceId, i.Id });
        });
    }
}

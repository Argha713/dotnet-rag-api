using Microsoft.EntityFrameworkCore;
using RagApi.Domain.Entities;

namespace RagApi.Infrastructure.Data;

// Argha - 2026-02-15 - EF Core DbContext for SQLite persistent storage
public class RagApiDbContext : DbContext
{
    public RagApiDbContext(DbContextOptions<RagApiDbContext> options) : base(options)
    {
    }

    public DbSet<Document> Documents => Set<Document>();

    // Argha - 2026-02-19 - Conversation sessions table 
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Argha - 2026-02-19 - ConversationSession model configuration 
        modelBuilder.Entity<ConversationSession>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Title)
                .HasMaxLength(200);

            entity.Property(s => s.MessagesJson)
                .IsRequired();

            entity.HasIndex(s => s.LastMessageAt);
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
        });
    }
}

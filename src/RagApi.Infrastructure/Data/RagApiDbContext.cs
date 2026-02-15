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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

            entity.HasIndex(d => d.UploadedAt);
        });
    }
}

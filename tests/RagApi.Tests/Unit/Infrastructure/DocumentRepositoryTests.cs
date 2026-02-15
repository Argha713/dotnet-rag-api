using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RagApi.Domain.Entities;
using RagApi.Infrastructure.Data;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-02-15 - Unit tests for DocumentRepository CRUD using in-memory EF Core (Phase 1.5)
public class DocumentRepositoryTests : IDisposable
{
    private readonly RagApiDbContext _dbContext;
    private readonly DocumentRepository _sut;

    public DocumentRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new RagApiDbContext(options);
        _sut = new DocumentRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AddAsync_InsertsDocument()
    {
        // Arrange
        var doc = CreateDocument("test.txt");

        // Act
        await _sut.AddAsync(doc);

        // Assert
        var saved = await _dbContext.Documents.FindAsync(doc.Id);
        saved.Should().NotBeNull();
        saved!.FileName.Should().Be("test.txt");
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsDocument()
    {
        // Arrange
        var doc = CreateDocument("found.txt");
        _dbContext.Documents.Add(doc);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(doc.Id);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be("found.txt");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrderedByUploadedAtDesc()
    {
        // Arrange
        var older = CreateDocument("older.txt");
        older.UploadedAt = DateTime.UtcNow.AddMinutes(-10);
        var newer = CreateDocument("newer.txt");
        newer.UploadedAt = DateTime.UtcNow;

        _dbContext.Documents.AddRange(older, newer);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].FileName.Should().Be("newer.txt");
        result[1].FileName.Should().Be("older.txt");
    }

    [Fact]
    public async Task UpdateAsync_ModifiesDocument()
    {
        // Arrange
        var doc = CreateDocument("original.txt");
        _dbContext.Documents.Add(doc);
        await _dbContext.SaveChangesAsync();
        _dbContext.Entry(doc).State = EntityState.Detached;

        // Act
        doc.Status = DocumentStatus.Completed;
        doc.ChunkCount = 5;
        await _sut.UpdateAsync(doc);

        // Assert
        var updated = await _dbContext.Documents.FindAsync(doc.Id);
        updated!.Status.Should().Be(DocumentStatus.Completed);
        updated.ChunkCount.Should().Be(5);
    }

    [Fact]
    public async Task DeleteAsync_RemovesDocument()
    {
        // Arrange
        var doc = CreateDocument("delete-me.txt");
        _dbContext.Documents.Add(doc);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAsync(doc.Id);

        // Assert
        var deleted = await _dbContext.Documents.FindAsync(doc.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_NoException()
    {
        // Act
        var act = () => _sut.DeleteAsync(Guid.NewGuid());

        // Assert
        await act.Should().NotThrowAsync();
    }

    private static Document CreateDocument(string fileName)
    {
        return new Document
        {
            FileName = fileName,
            ContentType = "text/plain",
            FileSize = 100,
            Status = DocumentStatus.Processing
        };
    }
}

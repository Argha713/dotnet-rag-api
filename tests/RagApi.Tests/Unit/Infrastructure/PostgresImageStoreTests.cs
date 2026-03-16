using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using RagApi.Infrastructure.Data;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-03-16 - #33 - Unit tests for PostgresImageStore using in-memory EF Core
public class PostgresImageStoreTests : IDisposable
{
    private readonly RagApiDbContext _dbContext;
    private readonly PostgresImageStore _sut;

    // Use Guid.Empty so images seeded directly via _dbContext (which default to Guid.Empty
    // WorkspaceId) match the workspace filter in PostgresImageStore
    private static readonly Guid TestWorkspaceId = Guid.Empty;

    public PostgresImageStoreTests()
    {
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new RagApiDbContext(options);

        var workspaceContext = new Mock<IWorkspaceContext>();
        workspaceContext.Setup(w => w.Current).Returns(new Workspace
        {
            Id = TestWorkspaceId,
            CollectionName = "documents"
        });

        _sut = new PostgresImageStore(_dbContext, workspaceContext.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task SaveAsync_InsertsRowAndReturnsId()
    {
        var image = CreateImage(documentId: Guid.NewGuid());

        var returnedId = await _sut.SaveAsync(image);

        returnedId.Should().Be(image.Id);
        var saved = await _dbContext.DocumentImages.FindAsync(image.Id);
        saved.Should().NotBeNull();
        saved!.ContentType.Should().Be("image/png");
        saved.WorkspaceId.Should().Be(TestWorkspaceId);
    }

    [Fact]
    public async Task SaveAsync_OverwritesCallerWorkspaceId()
    {
        // Argha - 2026-03-16 - #33 - SaveAsync must overwrite caller-supplied WorkspaceId
        // to prevent accidental cross-tenant writes
        var image = CreateImage(documentId: Guid.NewGuid());
        image.WorkspaceId = Guid.NewGuid(); // caller sets wrong workspace

        await _sut.SaveAsync(image);

        var saved = await _dbContext.DocumentImages.FindAsync(image.Id);
        saved!.WorkspaceId.Should().Be(TestWorkspaceId);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsImage()
    {
        var image = CreateImage(documentId: Guid.NewGuid());
        _dbContext.DocumentImages.Add(image);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAsync(image.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(image.Id);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WrongWorkspace_ReturnsNull()
    {
        // Argha - 2026-03-16 - #33 - Image belonging to a different workspace must not be returned
        var image = CreateImage(documentId: Guid.NewGuid());
        image.WorkspaceId = Guid.NewGuid(); // different workspace
        _dbContext.DocumentImages.Add(image);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAsync(image.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteByDocumentAsync_RemovesAllImagesForDocument()
    {
        var docId = Guid.NewGuid();
        var img1 = CreateImage(docId);
        var img2 = CreateImage(docId);
        _dbContext.DocumentImages.AddRange(img1, img2);
        await _dbContext.SaveChangesAsync();

        await _sut.DeleteByDocumentAsync(docId);

        var remaining = await _dbContext.DocumentImages
            .Where(i => i.DocumentId == docId)
            .ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteByDocumentAsync_LeavesOtherDocumentsImagesIntact()
    {
        var targetDocId = Guid.NewGuid();
        var otherDocId = Guid.NewGuid();
        _dbContext.DocumentImages.AddRange(CreateImage(targetDocId), CreateImage(otherDocId));
        await _dbContext.SaveChangesAsync();

        await _sut.DeleteByDocumentAsync(targetDocId);

        var remaining = await _dbContext.DocumentImages.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].DocumentId.Should().Be(otherDocId);
    }

    [Fact]
    public async Task DeleteByDocumentAsync_NonExistentDocument_NoException()
    {
        var act = () => _sut.DeleteByDocumentAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    private static DocumentImage CreateImage(Guid documentId) => new()
    {
        DocumentId = documentId,
        WorkspaceId = TestWorkspaceId,
        ContentType = "image/png",
        Data = new byte[] { 1, 2, 3 },
        PageNumber = 1
    };
}

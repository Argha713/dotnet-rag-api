// Argha - 2026-03-04 - #17 - Unit tests for WorkspaceRepository using in-memory EF Core
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RagApi.Domain.Entities;
using RagApi.Infrastructure.Data;

namespace RagApi.Tests.Unit.Infrastructure;

public class WorkspaceRepositoryTests : IDisposable
{
    private readonly RagApiDbContext _dbContext;
    private readonly WorkspaceRepository _sut;

    public WorkspaceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new RagApiDbContext(options);
        _sut = new WorkspaceRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_StoresWorkspace()
    {
        // Arrange
        var workspace = CreateWorkspace("Acme Corp");

        // Act
        await _sut.CreateAsync(workspace);

        // Assert
        var saved = await _dbContext.Workspaces.FindAsync(workspace.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsWorkspace()
    {
        // Arrange
        var workspace = CreateWorkspace("Test Corp");
        _dbContext.Workspaces.Add(workspace);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(workspace.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Corp");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByApiKeyHashAsync_ReturnsWorkspace_OnMatch()
    {
        // Arrange
        var workspace = CreateWorkspace("Hash Match Corp");
        workspace.HashedApiKey = "abc123hash";
        _dbContext.Workspaces.Add(workspace);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByApiKeyHashAsync("abc123hash");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Hash Match Corp");
    }

    [Fact]
    public async Task GetByApiKeyHashAsync_ReturnsNull_OnMismatch()
    {
        // Arrange
        var workspace = CreateWorkspace("Mismatch Corp");
        workspace.HashedApiKey = "correcthash";
        _dbContext.Workspaces.Add(workspace);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByApiKeyHashAsync("wronghash");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesWorkspace()
    {
        // Arrange
        var workspace = CreateWorkspace("To Delete");
        _dbContext.Workspaces.Add(workspace);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAsync(workspace.Id);

        // Assert
        var deleted = await _dbContext.Workspaces.FindAsync(workspace.Id);
        deleted.Should().BeNull();
    }

    private static Workspace CreateWorkspace(string name)
    {
        return new Workspace
        {
            Id = Guid.NewGuid(),
            Name = name,
            HashedApiKey = "testhash",
            CollectionName = $"ws_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
    }
}

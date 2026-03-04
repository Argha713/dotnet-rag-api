// Argha - 2026-03-04 - #17 - Unit tests for WorkspaceService lifecycle management
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IWorkspaceRepository> _workspaceRepoMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly WorkspaceService _sut;

    public WorkspaceServiceTests()
    {
        _workspaceRepoMock = new Mock<IWorkspaceRepository>();
        _vectorStoreMock = new Mock<IVectorStore>();

        _sut = new WorkspaceService(
            _workspaceRepoMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<WorkspaceService>>());
    }

    [Fact]
    public async Task CreateWorkspaceAsync_GeneratesUniqueKey_EachCall()
    {
        // Act
        var (_, key1) = await _sut.CreateWorkspaceAsync("Corp A");
        var (_, key2) = await _sut.CreateWorkspaceAsync("Corp B");

        // Assert — two calls must produce different plaintext keys
        key1.Should().NotBe(key2);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_StoresHashedKey_NotPlaintext()
    {
        // Arrange
        Workspace? capturedWorkspace = null;
        _workspaceRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Workspace>(), It.IsAny<CancellationToken>()))
            .Callback<Workspace, CancellationToken>((ws, _) => capturedWorkspace = ws)
            .ReturnsAsync((Workspace ws, CancellationToken _) => ws);

        // Act
        var (_, plaintext) = await _sut.CreateWorkspaceAsync("Acme");

        // Assert — the stored key must be the SHA-256 hash, not the plaintext
        capturedWorkspace.Should().NotBeNull();
        capturedWorkspace!.HashedApiKey.Should().NotBe(plaintext);
        capturedWorkspace.HashedApiKey.Should().HaveLength(64); // SHA-256 hex = 64 chars
    }

    [Fact]
    public async Task CreateWorkspaceAsync_CallsEnsureCollection()
    {
        // Act
        await _sut.CreateWorkspaceAsync("NewCorp");

        // Assert — EnsureCollectionAsync called for the new workspace's collection
        _vectorStoreMock.Verify(v => v.EnsureCollectionAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_CallsDeleteCollection()
    {
        // Arrange
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "ToDelete",
            CollectionName = "ws_abc123",
            HashedApiKey = "hash"
        };
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(workspace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);

        // Act
        await _sut.DeleteWorkspaceAsync(workspace.Id);

        // Assert — collection deleted from vector store
        _vectorStoreMock.Verify(v => v.DeleteCollectionAsync("ws_abc123", It.IsAny<CancellationToken>()), Times.Once);
        _workspaceRepoMock.Verify(r => r.DeleteAsync(workspace.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_ThrowsKeyNotFound_WhenMissing()
    {
        // Arrange
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);

        // Act
        var act = () => _sut.DeleteWorkspaceAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetWorkspaceAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);

        // Act
        var result = await _sut.GetWorkspaceAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }
}

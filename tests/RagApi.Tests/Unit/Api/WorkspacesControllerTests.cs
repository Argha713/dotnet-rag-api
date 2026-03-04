// Argha - 2026-03-04 - #17 - Unit tests for WorkspacesController CRUD endpoints
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Api.Models;
using RagApi.Application.Interfaces;
using RagApi.Application.Services;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

public class WorkspacesControllerTests
{
    private readonly Mock<IWorkspaceRepository> _workspaceRepoMock;
    private readonly Mock<IVectorStore> _vectorStoreMock;
    private readonly Mock<IWorkspaceContext> _workspaceContextMock;
    private readonly WorkspacesController _sut;

    public WorkspacesControllerTests()
    {
        _workspaceRepoMock = new Mock<IWorkspaceRepository>();
        _vectorStoreMock = new Mock<IVectorStore>();
        _workspaceContextMock = new Mock<IWorkspaceContext>();

        // Argha - 2026-03-04 - #17 - Default: caller uses the global (default) workspace key
        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = "documents"
        });

        var workspaceService = new WorkspaceService(
            _workspaceRepoMock.Object,
            _vectorStoreMock.Object,
            Mock.Of<ILogger<WorkspaceService>>());

        _sut = new WorkspacesController(workspaceService, _workspaceContextMock.Object);
    }

    // ── POST /api/workspaces ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateWorkspace_Returns201_WithApiKey()
    {
        // Act
        var result = await _sut.CreateWorkspace(new CreateWorkspaceRequest("Acme Corp"), CancellationToken.None);

        // Assert
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        var dto = created.Value.Should().BeOfType<WorkspaceCreatedDto>().Subject;
        dto.Name.Should().Be("Acme Corp");
        dto.ApiKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateWorkspace_Returns400_WhenNameEmpty()
    {
        // Act
        var result = await _sut.CreateWorkspace(new CreateWorkspaceRequest(""), CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateWorkspace_Returns400_WhenNameNull()
    {
        // Act
        var result = await _sut.CreateWorkspace(new CreateWorkspaceRequest(null!), CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateWorkspace_ApiKeyInResponse_IsPlaintextHex64Chars()
    {
        // Act
        var result = await _sut.CreateWorkspace(new CreateWorkspaceRequest("TestCorp"), CancellationToken.None);

        // Assert — plaintext key is 32 random bytes hex-encoded = 64 chars
        var dto = ((CreatedAtActionResult)result).Value as WorkspaceCreatedDto;
        dto!.ApiKey.Should().HaveLength(64);
        dto.ApiKey.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ── GET /api/workspaces/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task GetWorkspace_Returns200_WithDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = id, Name = "Found", CollectionName = "ws_found" });

        // Act
        var result = await _sut.GetWorkspace(id, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<WorkspaceDto>().Subject;
        dto.Id.Should().Be(id);
        dto.Name.Should().Be("Found");
    }

    [Fact]
    public async Task GetWorkspace_Returns404_WhenNotFound()
    {
        // Arrange
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);

        // Act
        var result = await _sut.GetWorkspace(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    // ── DELETE /api/workspaces/{id} ────────────────────────────────────────────

    [Fact]
    public async Task DeleteWorkspace_Returns204_OnSuccess()
    {
        // Arrange
        var id = Guid.NewGuid();
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Workspace { Id = id, Name = "ToDelete", CollectionName = "ws_del" });

        // Act — global key (DefaultWorkspaceId) can delete any workspace
        var result = await _sut.DeleteWorkspace(id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteWorkspace_Returns404_WhenNotFound()
    {
        // Arrange
        _workspaceRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Workspace?)null);

        // Act
        var result = await _sut.DeleteWorkspace(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DeleteWorkspace_Returns403_WhenWrongWorkspaceKey()
    {
        // Arrange — caller uses a non-default workspace key that doesn't own the target ID
        var callerWorkspaceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = callerWorkspaceId,
            CollectionName = "ws_caller"
        });

        // Act
        var result = await _sut.DeleteWorkspace(targetId, CancellationToken.None);

        // Assert — 403 because caller does not own target and is not the global key
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}

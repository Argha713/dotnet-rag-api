// Argha - 2026-03-17 - #37 - Integration tests for PostgresImageStore.GetStreamAsync.
// Requires a live PostgreSQL instance. Kept separate from PostgresImageStoreTests.cs
// because those tests use EF Core InMemory which cannot back raw Npgsql queries.
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;
using RagApi.Infrastructure.Data;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-03-17 - #37 - All tests marked Integration; run with:
//   dotnet test --filter "Category=Integration"
// Skipped by default CI filter:
//   dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")]
public class PostgresImageStoreStreamTests : IAsyncLifetime
{
    private readonly RagApiDbContext _dbContext;
    private readonly NpgsqlDataSource _dataSource;
    private readonly PostgresImageStore _sut;
    private readonly Mock<IWorkspaceContext> _workspaceContextMock;

    // Argha - 2026-03-17 - #37 - Unique per test run to avoid collisions with existing data
    private static readonly Guid TestWorkspaceId = Guid.NewGuid();

    // Argha - 2026-03-17 - #37 - Parent rows seeded once for the whole class; cleaned up in DisposeAsync
    private Guid _testDocumentId = Guid.NewGuid();
    private readonly List<Guid> _seededImageIds = new();
    // Argha - 2026-03-17 - #39 - Extra workspace rows seeded by individual tests; cleaned up in DisposeAsync
    private readonly List<Guid> _seededWorkspaceIds = new();

    public PostgresImageStoreStreamTests()
    {
        // Argha - 2026-03-17 - #37 - Load connection string from appsettings.json or env var.
        // ConfigurationBuilder picks up the file copied to the test output directory.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ragapi;Username=ragapi;Password=changeme";

        // Argha - 2026-03-17 - #37 - NpgsqlDataSource for streaming (real Npgsql path used by GetStreamAsync)
        _dataSource = NpgsqlDataSource.Create(connectionString);

        // Argha - 2026-03-17 - #37 - Real EF Core context backed by real PostgreSQL for seeding test data
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        _dbContext = new RagApiDbContext(options);

        _workspaceContextMock = new Mock<IWorkspaceContext>();
        _workspaceContextMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = TestWorkspaceId,
            CollectionName = "documents",
            Name = "test-workspace",
            HashedApiKey = Guid.NewGuid().ToString("N")
        });

        _sut = new PostgresImageStore(_dbContext, _workspaceContextMock.Object, _dataSource);
    }

    // Argha - 2026-03-17 - #37 - Seed the Workspace and Document parent rows required by FK constraints.
    // DocumentImages has FK → Workspaces and FK → Documents; both must exist before inserting images.
    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var workspace = new Workspace
        {
            Id = TestWorkspaceId,
            Name = "test-workspace-stream",
            HashedApiKey = Guid.NewGuid().ToString("N"),
            CollectionName = "documents",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Workspaces.Add(workspace);

        var document = new Document
        {
            Id = _testDocumentId,
            FileName = "test-stream.txt",
            ContentType = "text/plain",
            WorkspaceId = TestWorkspaceId,
            UploadedAt = DateTime.UtcNow
        };
        _dbContext.Documents.Add(document);

        await _dbContext.SaveChangesAsync();
    }

    // Argha - 2026-03-17 - #37 - Clean up ALL rows seeded by this test class in reverse FK order.
    public async Task DisposeAsync()
    {
        // Argha - 2026-03-17 - #37 - Delete seeded images first (child), then document, then workspace
        if (_seededImageIds.Count > 0)
        {
            var images = await _dbContext.DocumentImages
                .Where(i => _seededImageIds.Contains(i.Id))
                .ToListAsync();
            _dbContext.DocumentImages.RemoveRange(images);
            await _dbContext.SaveChangesAsync();
        }

        // Argha - 2026-03-17 - #39 - Clean up extra workspace rows seeded by individual tests
        foreach (var wsId in _seededWorkspaceIds)
        {
            await _dbContext.Set<Workspace>().Where(w => w.Id == wsId).ExecuteDeleteAsync();
        }

        var doc = await _dbContext.Documents.FindAsync(_testDocumentId);
        if (doc is not null)
        {
            _dbContext.Documents.Remove(doc);
            await _dbContext.SaveChangesAsync();
        }

        var ws = await _dbContext.Workspaces.FindAsync(TestWorkspaceId);
        if (ws is not null)
        {
            _dbContext.Workspaces.Remove(ws);
            await _dbContext.SaveChangesAsync();
        }

        await _dbContext.DisposeAsync();
        await _dataSource.DisposeAsync();
    }

    // Argha - 2026-03-17 - #37 - Helper to seed a DocumentImage directly via EF Core (bypasses
    // PostgresImageStore.SaveAsync so WorkspaceId is not overwritten by the store)
    private async Task<DocumentImage> SeedImageAsync(
        Guid workspaceId,
        string contentType = "image/png",
        byte[]? data = null)
    {
        var image = new DocumentImage
        {
            Id = Guid.NewGuid(),
            DocumentId = _testDocumentId,
            WorkspaceId = workspaceId,
            ContentType = contentType,
            Data = data ?? new byte[] { 0xFF },
            PageNumber = 1,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.DocumentImages.Add(image);
        await _dbContext.SaveChangesAsync();

        // Argha - 2026-03-17 - #37 - Track for cleanup in DisposeAsync
        _seededImageIds.Add(image.Id);
        return image;
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStreamAsync_ReturnsNull_WhenNoRowMatchesId()
    {
        // Argha - 2026-03-17 - #37 - No row seeded; random GUID guaranteed to be absent
        var ct = CancellationToken.None;

        var result = await _sut.GetStreamAsync(Guid.NewGuid(), ct);

        result.Should().BeNull();
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStreamAsync_ReturnsImage_WhenImageBelongsToDifferentWorkspace()
    {
        // Argha - 2026-03-17 - #39 - Workspace filter removed from GetStreamAsync; GUID is the
        // capability token so an image in another workspace must still be reachable by ID
        var otherWorkspaceId = Guid.NewGuid();

        // Argha - 2026-03-17 - #39 - Seed a second Workspace row so the FK constraint is satisfied
        var otherWorkspace = new Workspace
        {
            Id = otherWorkspaceId,
            Name = "other-workspace-stream",
            HashedApiKey = Guid.NewGuid().ToString("N"),
            CollectionName = "other-documents",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Workspaces.Add(otherWorkspace);
        await _dbContext.SaveChangesAsync();
        // Argha - 2026-03-17 - #39 - Track for cleanup in DisposeAsync (consistent with _seededImageIds pattern)
        _seededWorkspaceIds.Add(otherWorkspaceId);

        var image = await SeedImageAsync(workspaceId: otherWorkspaceId);

        var result = await _sut.GetStreamAsync(image.Id, CancellationToken.None);

        try
        {
            // Argha - 2026-03-17 - #39 - No workspace filter: image found regardless of workspace
            result.Should().NotBeNull();
        }
        finally
        {
            if (result is not null)
                await result.DisposeAsync();
        }
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStreamAsync_ReturnsCorrectContentType_WhenRowExists()
    {
        // Argha - 2026-03-17 - #37 - Seed an image owned by TestWorkspaceId with known ContentType
        var image = await SeedImageAsync(workspaceId: TestWorkspaceId, contentType: "image/png");

        var result = await _sut.GetStreamAsync(image.Id, CancellationToken.None);

        try
        {
            result.Should().NotBeNull();
            result!.ContentType.Should().Be("image/png");
        }
        finally
        {
            // Argha - 2026-03-17 - #37 - Dispose closes the Npgsql connection held by NpgsqlImageStream
            if (result is not null)
                await result.DisposeAsync();
        }
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStreamAsync_ReturnedBody_IsReadable()
    {
        // Argha - 2026-03-17 - #37 - Seed an image with known byte content
        var expectedBytes = new byte[] { 1, 2, 3 };
        var image = await SeedImageAsync(
            workspaceId: TestWorkspaceId,
            contentType: "image/jpeg",
            data: expectedBytes);

        var result = await _sut.GetStreamAsync(image.Id, CancellationToken.None);

        try
        {
            result.Should().NotBeNull();

            using var ms = new MemoryStream();
            await result!.Body.CopyToAsync(ms);
            var actualBytes = ms.ToArray();

            actualBytes.Should().Equal(expectedBytes);
        }
        finally
        {
            // Argha - 2026-03-17 - #37 - Dispose closes reader + Npgsql connection
            if (result is not null)
                await result.DisposeAsync();
        }
    }
}

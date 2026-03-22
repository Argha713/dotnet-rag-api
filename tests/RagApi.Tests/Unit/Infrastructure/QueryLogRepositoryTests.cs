using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RagApi.Application.Models;
using RagApi.Domain.Entities;
using RagApi.Infrastructure.Data;

namespace RagApi.Tests.Unit.Infrastructure;

// Argha - 2026-03-22 - #92 - EF Core InMemory tests for QueryLogRepository
public class QueryLogRepositoryTests : IDisposable
{
    private readonly RagApiDbContext _dbContext;
    private readonly QueryLogRepository _sut;

    public QueryLogRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<RagApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new RagApiDbContext(options);
        _sut = new QueryLogRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    private static QueryLog MakeLog(Guid workspaceId, DateTime loggedAt, long latencyMs = 100, decimal cost = 0m)
        => new QueryLog
        {
            WorkspaceId = workspaceId,
            QueryType = QueryType.Chat,
            QueryText = "test query",
            LatencyMs = latencyMs,
            EstimatedCostUsd = cost,
            LoggedAt = loggedAt
        };

    [Fact]
    public async Task AddAsync_PersistsLogToDatabase()
    {
        var wsId = Guid.NewGuid();
        var log = MakeLog(wsId, DateTime.UtcNow);

        await _sut.AddAsync(log, CancellationToken.None);

        var saved = await _dbContext.QueryLogs.FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.WorkspaceId.Should().Be(wsId);
    }

    [Fact]
    public async Task GetAnalyticsAsync_ReturnsCorrectTotals()
    {
        // Argha - 2026-03-22 - #92 - Two logs: latency 100+200=300ms avg=150ms
        var wsId = Guid.NewGuid();
        await _sut.AddAsync(MakeLog(wsId, DateTime.UtcNow, latencyMs: 100, cost: 0.001m), CancellationToken.None);
        await _sut.AddAsync(MakeLog(wsId, DateTime.UtcNow, latencyMs: 200, cost: 0.002m), CancellationToken.None);

        var dto = await _sut.GetAnalyticsAsync(wsId, 7, CancellationToken.None);

        dto.TotalQueries.Should().Be(2);
        dto.TotalCostUsd.Should().Be(0.003m);
        dto.AvgLatencyMs.Should().Be(150.0);
    }

    [Fact]
    public async Task GetAnalyticsAsync_FiltersToWorkspace()
    {
        // Argha - 2026-03-22 - #92 - Two workspaces; only wsId1 logs should be returned
        var wsId1 = Guid.NewGuid();
        var wsId2 = Guid.NewGuid();
        await _sut.AddAsync(MakeLog(wsId1, DateTime.UtcNow), CancellationToken.None);
        await _sut.AddAsync(MakeLog(wsId2, DateTime.UtcNow), CancellationToken.None);

        var dto = await _sut.GetAnalyticsAsync(wsId1, 7, CancellationToken.None);

        dto.TotalQueries.Should().Be(1);
    }

    [Fact]
    public async Task GetAnalyticsAsync_FiltersToDateRange()
    {
        // Argha - 2026-03-22 - #92 - Old log (10 days ago) excluded from 7-day window
        var wsId = Guid.NewGuid();
        await _sut.AddAsync(MakeLog(wsId, DateTime.UtcNow), CancellationToken.None);
        await _sut.AddAsync(MakeLog(wsId, DateTime.UtcNow.AddDays(-10)), CancellationToken.None);

        var dto = await _sut.GetAnalyticsAsync(wsId, 7, CancellationToken.None);

        dto.TotalQueries.Should().Be(1);
    }

    [Fact]
    public async Task GetAnalyticsAsync_EmptyData_ReturnsZeroTotals()
    {
        var wsId = Guid.NewGuid();

        var dto = await _sut.GetAnalyticsAsync(wsId, 7, CancellationToken.None);

        dto.TotalQueries.Should().Be(0);
        dto.TotalCostUsd.Should().Be(0m);
        dto.AvgLatencyMs.Should().Be(0.0);
        dto.DailyBreakdown.Should().BeEmpty();
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Api;

// Argha - 2026-03-22 - #92 - Unit tests for AnalyticsController
public class AnalyticsControllerTests
{
    private static (AnalyticsController controller, Mock<IQueryLogRepository> repoMock)
        CreateController(AnalyticsDto? dto = null)
    {
        var repoMock = new Mock<IQueryLogRepository>();
        repoMock
            .Setup(r => r.GetAnalyticsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto ?? new AnalyticsDto { TotalQueries = 5, AvgLatencyMs = 120.0 });

        var wsMock = new Mock<IWorkspaceContext>();
        wsMock.Setup(w => w.Current).Returns(new Workspace
        {
            Id = Workspace.DefaultWorkspaceId,
            CollectionName = "documents"
        });

        var controller = new AnalyticsController(repoMock.Object, wsMock.Object);
        return (controller, repoMock);
    }

    [Fact]
    public async Task GetAnalytics_Returns200WithDto()
    {
        var (controller, _) = CreateController(new AnalyticsDto { TotalQueries = 3 });

        var result = await controller.GetAnalytics(7, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<AnalyticsDto>()
            .Which.TotalQueries.Should().Be(3);
    }

    [Fact]
    public async Task GetAnalytics_DefaultDays_IsSevenDays()
    {
        // Argha - 2026-03-22 - #92 - Verify default of 7 days is passed to repository
        var (controller, repoMock) = CreateController();

        await controller.GetAnalytics(cancellationToken: CancellationToken.None);

        repoMock.Verify(r =>
            r.GetAnalyticsAsync(It.IsAny<Guid>(), 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAnalytics_DaysAbove30_ClampsTo30()
    {
        var (controller, repoMock) = CreateController();

        await controller.GetAnalytics(100, CancellationToken.None);

        repoMock.Verify(r =>
            r.GetAnalyticsAsync(It.IsAny<Guid>(), 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAnalytics_DaysBelow1_ClampsTo1()
    {
        var (controller, repoMock) = CreateController();

        await controller.GetAnalytics(0, CancellationToken.None);

        repoMock.Verify(r =>
            r.GetAnalyticsAsync(It.IsAny<Guid>(), 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAnalytics_EmptyDailyBreakdown_Returns200WithEmptyList()
    {
        var (controller, _) = CreateController(new AnalyticsDto
        {
            TotalQueries = 0,
            DailyBreakdown = new List<DailyAnalyticsDto>()
        });

        var result = await controller.GetAnalytics(7, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<AnalyticsDto>().Subject;
        dto.DailyBreakdown.Should().BeEmpty();
    }
}

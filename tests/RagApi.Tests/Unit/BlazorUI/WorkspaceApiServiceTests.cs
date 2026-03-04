// Argha - 2026-03-04 - #17 - Unit tests for WorkspaceApiService HTTP interactions
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagApi.BlazorUI.Models;
using RagApi.BlazorUI.Services;

namespace RagApi.Tests.Unit.BlazorUI;

public class WorkspaceApiServiceTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private static (WorkspaceApiService sut, Mock<HttpMessageHandler> handler) BuildSut(
        HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5000") };
        return (new WorkspaceApiService(client), handler);
    }

    [Fact]
    public async Task CreateAsync_PostsToWorkspacesEndpoint()
    {
        var created = new WorkspaceCreatedDto(Guid.NewGuid(), "Acme", DateTime.UtcNow, "ws_abc", "key123");
        var (sut, handler) = BuildSut(HttpStatusCode.Created, JsonSerializer.Serialize(created, _opts));

        var result = await sut.CreateAsync("Acme");

        result.Name.Should().Be("Acme");
        result.ApiKey.Should().Be("key123");
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery == "/api/workspaces"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsWorkspaceDto()
    {
        var dto = new WorkspaceDto(Guid.NewGuid(), "Test Corp", DateTime.UtcNow, "ws_abc");
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(dto, _opts));

        var result = await sut.GetCurrentAsync();

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Corp");
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsFalse_OnUnauthorized()
    {
        var (sut, _) = BuildSut(HttpStatusCode.Unauthorized, string.Empty);

        var result = await sut.ValidateKeyAsync("bad-key");

        result.Should().BeFalse();
    }
}

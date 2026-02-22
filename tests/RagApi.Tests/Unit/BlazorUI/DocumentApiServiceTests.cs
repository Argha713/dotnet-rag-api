using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagApi.BlazorUI.Models;
using RagApi.BlazorUI.Services;

namespace RagApi.Tests.Unit.BlazorUI;

// Argha - 2026-02-21 - Unit tests for DocumentApiService 
public class DocumentApiServiceTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private static (DocumentApiService sut, Mock<HttpMessageHandler> handler) BuildSut(
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
        return (new DocumentApiService(client), handler);
    }

    [Fact]
    public async Task GetDocumentsAsync_ReturnsDeserializedList()
    {
        var expected = new List<DocumentDto>
        {
            new() { Id = Guid.NewGuid(), FileName = "report.pdf", Status = "Completed", ChunkCount = 10 }
        };
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(expected, _opts));

        var result = await sut.GetDocumentsAsync();

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("report.pdf");
        result[0].ChunkCount.Should().Be(10);
    }

    [Fact]
    public async Task GetDocumentsAsync_WithTagFilter_AppendsQueryParameter()
    {
        var (sut, handler) = BuildSut(HttpStatusCode.OK, "[]");

        await sut.GetDocumentsAsync("finance");

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Get &&
                r.RequestUri!.PathAndQuery.Contains("tag=finance")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DeleteDocumentAsync_SendsDeleteRequest()
    {
        var id = Guid.NewGuid();
        var (sut, handler) = BuildSut(HttpStatusCode.NoContent, string.Empty);

        await sut.DeleteDocumentAsync(id);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Delete &&
                r.RequestUri!.PathAndQuery == $"/api/documents/{id}"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetSupportedTypesAsync_ReturnsDeserializedList()
    {
        var expected = new List<string> { "application/pdf", "text/plain" };
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(expected, _opts));

        var result = await sut.GetSupportedTypesAsync();

        result.Should().BeEquivalentTo(expected);
    }
}

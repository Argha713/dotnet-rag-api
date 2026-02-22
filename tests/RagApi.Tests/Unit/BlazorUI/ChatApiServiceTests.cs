using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagApi.BlazorUI.Models;
using RagApi.BlazorUI.Services;

namespace RagApi.Tests.Unit.BlazorUI;

// Argha - 2026-02-21 - Unit tests for ChatApiService 
public class ChatApiServiceTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private static (ChatApiService sut, Mock<HttpMessageHandler> handler) BuildSut(
        HttpStatusCode status, string body, string contentType = "application/json")
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
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5000") };
        return (new ChatApiService(client), handler);
    }

    [Fact]
    public async Task ChatAsync_ValidRequest_DeserializesResponse()
    {
        var expected = new ChatResponseDto { Answer = "Blazor is great", Model = "llama3.2" };
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(expected, _opts));

        var result = await sut.ChatAsync(new ChatRequest { Query = "What is Blazor?" });

        result.Answer.Should().Be("Blazor is great");
        result.Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task ChatAsync_PostsToCorrectEndpoint()
    {
        var expected = new ChatResponseDto { Answer = "ok" };
        var (sut, handler) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(expected, _opts));

        await sut.ChatAsync(new ChatRequest { Query = "test" });

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery == "/api/chat"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_ValidRequest_DeserializesResponse()
    {
        var expected = new List<SearchResultDto>
        {
            new() { FileName = "doc.pdf", Content = "relevant text", Score = 0.9 }
        };
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(expected, _opts));

        var result = await sut.SearchAsync(new SearchRequest { Query = "blazor" });

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("doc.pdf");
        result[0].Score.Should().Be(0.9);
    }

    [Fact]
    public async Task ChatStreamAsync_ParsesSseEvents_InvokesCallbacks()
    {
        // Argha - 2026-02-21 - Build a minimal SSE body matching the API event format
        var sourcesEvt = JsonSerializer.Serialize(new UiStreamEvent
        {
            Type = "sources",
            Sources = new List<SourceCitationDto>(),
            Model = "llama3.2"
        }, _opts);
        var tokenEvt = JsonSerializer.Serialize(new UiStreamEvent { Type = "token", Content = "Hello" }, _opts);
        var doneEvt  = JsonSerializer.Serialize(new UiStreamEvent { Type = "done" }, _opts);

        var sseBody = $"data: {sourcesEvt}\n\ndata: {tokenEvt}\n\ndata: {doneEvt}\n\n";
        var (sut, _) = BuildSut(HttpStatusCode.OK, sseBody, "text/event-stream");

        var events = new List<UiStreamEvent>();
        await sut.ChatStreamAsync(new ChatRequest { Query = "hi" }, evt => events.Add(evt));

        events.Should().HaveCount(3);
        events[0].Type.Should().Be("sources");
        events[0].Model.Should().Be("llama3.2");
        events[1].Type.Should().Be("token");
        events[1].Content.Should().Be("Hello");
        events[2].Type.Should().Be("done");
    }
}

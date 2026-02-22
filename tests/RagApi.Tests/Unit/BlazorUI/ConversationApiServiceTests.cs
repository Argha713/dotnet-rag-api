using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Moq.Protected;
using RagApi.BlazorUI.Models;
using RagApi.BlazorUI.Services;

namespace RagApi.Tests.Unit.BlazorUI;

// Argha - 2026-02-21 - Unit tests for ConversationApiService 
public class ConversationApiServiceTests
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private static (ConversationApiService sut, Mock<HttpMessageHandler> handler) BuildSut(
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
        return (new ConversationApiService(client), handler);
    }

    [Fact]
    public async Task CreateAsync_PostsToConversationsEndpoint()
    {
        var sessionId = Guid.NewGuid();
        var payload = new CreateSessionResponse { SessionId = sessionId, CreatedAt = DateTime.UtcNow };
        var (sut, handler) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(payload, _opts));

        var result = await sut.CreateAsync();

        result.SessionId.Should().Be(sessionId);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery == "/api/conversations"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ReturnsDeserializedConversation()
    {
        var id = Guid.NewGuid();
        var session = new SessionDto
        {
            SessionId = id,
            CreatedAt  = DateTime.UtcNow,
            Messages   = new List<SessionMessageDto>
            {
                new() { Role = "user",      Content = "Hello" },
                new() { Role = "assistant", Content = "Hi!"   }
            }
        };
        var (sut, _) = BuildSut(HttpStatusCode.OK, JsonSerializer.Serialize(session, _opts));

        var result = await sut.GetAsync(id);

        result.SessionId.Should().Be(id);
        result.Messages.Should().HaveCount(2);
        result.Messages[0].Role.Should().Be("user");
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var id = Guid.NewGuid();
        var (sut, handler) = BuildSut(HttpStatusCode.NoContent, string.Empty);

        await sut.DeleteAsync(id);

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Delete &&
                r.RequestUri!.PathAndQuery == $"/api/conversations/{id}"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ThrowsOnNonSuccessStatusCode()
    {
        var (sut, _) = BuildSut(HttpStatusCode.NotFound, string.Empty);

        var act = async () => await sut.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

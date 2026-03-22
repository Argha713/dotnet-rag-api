// Argha - 2026-03-22 - #83 - Tests for NullAudioTranscriptionService and OpenAiWhisperService
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using RagApi.Application.Models;
using RagApi.Infrastructure;
using RagApi.Infrastructure.AI;

namespace RagApi.Tests.Unit.Infrastructure;

public class AudioTranscriptionTests
{
    [Fact]
    public void NullAudioTranscriptionService_IsSupported_ReturnsFalse()
    {
        // Argha - 2026-03-22 - #83 - NullAudioTranscriptionService must advertise no support (Ollama path)
        var sut = new NullAudioTranscriptionService();
        sut.IsSupported.Should().BeFalse();
    }

    [Fact]
    public async Task NullAudioTranscriptionService_TranscribeAsync_ThrowsNotSupportedException()
    {
        // Argha - 2026-03-22 - #83 - Calling TranscribeAsync on the null service must throw NotSupportedException
        var sut = new NullAudioTranscriptionService();
        var act = () => sut.TranscribeAsync(new MemoryStream(), "audio/mpeg");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void OpenAiWhisperService_IsSupported_ReturnsTrue()
    {
        // Argha - 2026-03-22 - #83 - OpenAI Whisper service must advertise support (used when Provider = OpenAI)
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        var aiConfig = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings { BaseUrl = "https://api.openai.com/v1", ApiKey = "test-key" }
        });
        var audioConfig = Options.Create(new AudioTranscriptionOptions());
        var logger = Mock.Of<ILogger<OpenAiWhisperService>>();

        var sut = new OpenAiWhisperService(new HttpClient(new Mock<HttpMessageHandler>().Object), aiConfig, audioConfig, logger);

        sut.IsSupported.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAiWhisperService_TranscribeAsync_HttpError_ThrowsHttpRequestException()
    {
        // Argha - 2026-03-22 - #83 - A non-2xx response from Whisper API must propagate as HttpRequestException
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));

        var aiConfig = Options.Create(new AiConfiguration
        {
            OpenAi = new OpenAiSettings { BaseUrl = "https://api.openai.com/v1", ApiKey = "test-key" }
        });
        var audioConfig = Options.Create(new AudioTranscriptionOptions());
        var logger = Mock.Of<ILogger<OpenAiWhisperService>>();

        var sut = new OpenAiWhisperService(new HttpClient(handler.Object), aiConfig, audioConfig, logger);

        var act = () => sut.TranscribeAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "audio/mpeg");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

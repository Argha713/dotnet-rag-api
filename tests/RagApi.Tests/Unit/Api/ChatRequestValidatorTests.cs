using FluentAssertions;
using RagApi.Api.Models;
using RagApi.Api.Validators;

namespace RagApi.Tests.Unit.Api;

public class ChatRequestValidatorTests
{
    private readonly ChatRequestValidator _sut = new();

    private static ChatRequest ValidRequest() => new()
    {
        Query = "What is RAG?",
        TopK = 5
    };

    // ── Tags ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_NullTags_IsValid()
    {
        var request = ValidRequest();
        request.Tags = null;

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyTagsList_IsValid()
    {
        var request = ValidRequest();
        request.Tags = new List<string>();

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_TagWithEmptyString_IsInvalid()
    {
        var request = ValidRequest();
        request.Tags = new List<string> { "valid-tag", "" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("tag must not be empty"));
    }

    [Fact]
    public async Task Validate_TagExceeding100Chars_IsInvalid()
    {
        var request = ValidRequest();
        request.Tags = new List<string> { new string('x', 101) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("100 characters"));
    }

    [Fact]
    public async Task Validate_TagsListExceeding20Items_IsInvalid()
    {
        var request = ValidRequest();
        request.Tags = Enumerable.Range(1, 21).Select(i => $"tag{i}").ToList();

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("20 items"));
    }

    [Fact]
    public async Task Validate_TagsListWithExactly20Items_IsValid()
    {
        var request = ValidRequest();
        request.Tags = Enumerable.Range(1, 20).Select(i => $"tag{i}").ToList();

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    // ── ConversationHistory ───────────────────────────────────────────────────

    [Fact]
    public async Task Validate_NullConversationHistory_IsValid()
    {
        var request = ValidRequest();
        request.ConversationHistory = null;

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ConversationHistoryWithInvalidRole_IsInvalid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "admin", Content = "some content" }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("'user' or 'assistant'"));
    }

    [Fact]
    public async Task Validate_ConversationHistoryWithEmptyRole_IsInvalid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "", Content = "some content" }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ConversationHistoryWithEmptyContent_IsInvalid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "" }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Content must not be empty"));
    }

    [Fact]
    public async Task Validate_ConversationHistoryWithContentExceeding10000Chars_IsInvalid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "user", Content = new string('a', 10001) }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("10,000 characters"));
    }

    [Fact]
    public async Task Validate_ConversationHistoryWithValidUserAndAssistantRoles_IsValid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "user", Content = "Hello" },
            new() { Role = "assistant", Content = "Hi there" }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RoleIsCaseInsensitive_IsValid()
    {
        var request = ValidRequest();
        request.ConversationHistory = new List<ConversationMessage>
        {
            new() { Role = "USER", Content = "Hello" },
            new() { Role = "ASSISTANT", Content = "Hi" }
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}

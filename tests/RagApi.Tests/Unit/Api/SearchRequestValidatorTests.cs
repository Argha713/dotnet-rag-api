using FluentAssertions;
using RagApi.Api.Models;
using RagApi.Api.Validators;

namespace RagApi.Tests.Unit.Api;

public class SearchRequestValidatorTests
{
    private readonly SearchRequestValidator _sut = new();

    private static SearchRequest ValidRequest() => new()
    {
        Query = "machine learning",
        TopK = 5
    };

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
        request.Tags = new List<string> { "good-tag", "" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("tag must not be empty"));
    }

    [Fact]
    public async Task Validate_TagExceeding100Chars_IsInvalid()
    {
        var request = ValidRequest();
        request.Tags = new List<string> { new string('z', 101) };

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

    [Fact]
    public async Task Validate_ValidTagsList_IsValid()
    {
        var request = ValidRequest();
        request.Tags = new List<string> { "science", "ai", "dotnet" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MixedValidAndInvalidTags_OnlyInvalidItemsFail()
    {
        var request = ValidRequest();
        request.Tags = new List<string> { "valid-tag", new string('x', 101), "another-valid" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
    }
}

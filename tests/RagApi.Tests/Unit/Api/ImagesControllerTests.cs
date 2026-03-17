// Argha - 2026-03-17 - #37 - Unit tests for ImagesController GET /api/images/{id}
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RagApi.Api.Controllers;
using RagApi.Application.Interfaces;
using RagApi.Application.Models;

namespace RagApi.Tests.Unit.Api;

public class ImagesControllerTests
{
    private readonly Mock<IImageStore> _imageStoreMock;
    private readonly ImagesController _sut;
    private readonly DefaultHttpContext _httpContext;

    public ImagesControllerTests()
    {
        _imageStoreMock = new Mock<IImageStore>();
        _sut = new ImagesController(_imageStoreMock.Object);
        _httpContext = new DefaultHttpContext();
        _sut.ControllerContext = new ControllerContext { HttpContext = _httpContext };
    }

    [Fact]
    public async Task GetImage_Returns200WithFileResult_WhenImageExists()
    {
        // Argha - 2026-03-17 - #37 - Happy path: store returns a stream, controller wraps it in FileStreamResult
        var imageId = Guid.NewGuid();
        var body = new MemoryStream(new byte[] { 1, 2, 3 });
        var streamResult = new ImageStreamResult(body, "image/jpeg");
        _imageStoreMock
            .Setup(s => s.GetStreamAsync(imageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(streamResult);

        var result = await _sut.GetImage(imageId, CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileStream.Should().BeSameAs(body);
    }

    [Fact]
    public async Task GetImage_SetsCorrectCacheControlHeader_WhenImageExists()
    {
        // Argha - 2026-03-17 - #37 - Images are immutable; Cache-Control set to 1 day
        var imageId = Guid.NewGuid();
        using var streamResult = new ImageStreamResult(new MemoryStream(new byte[] { 0 }), "image/png");
        _imageStoreMock
            .Setup(s => s.GetStreamAsync(imageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(streamResult);

        await _sut.GetImage(imageId, CancellationToken.None);

        _httpContext.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=86400");
    }

    [Fact]
    public async Task GetImage_Returns404_WhenStoreReturnsNull()
    {
        // Argha - 2026-03-17 - #37 - Missing or wrong-workspace image returns 404 with no body
        var imageId = Guid.NewGuid();
        _imageStoreMock
            .Setup(s => s.GetStreamAsync(imageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImageStreamResult?)null);

        var result = await _sut.GetImage(imageId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetImage_PassesCorrectIdToStore()
    {
        // Argha - 2026-03-17 - #37 - Verify the exact Guid from the route is forwarded
        var expectedId = Guid.NewGuid();
        using var streamResult = new ImageStreamResult(new MemoryStream(), "image/png");
        _imageStoreMock
            .Setup(s => s.GetStreamAsync(expectedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(streamResult);

        await _sut.GetImage(expectedId, CancellationToken.None);

        _imageStoreMock.Verify(s => s.GetStreamAsync(expectedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetImage_SetsCorrectContentType_ForPng()
    {
        // Argha - 2026-03-17 - #37 - ContentType is dynamic — taken from the stored image record
        var imageId = Guid.NewGuid();
        using var streamResult = new ImageStreamResult(new MemoryStream(new byte[] { 0 }), "image/png");
        _imageStoreMock
            .Setup(s => s.GetStreamAsync(imageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(streamResult);

        var result = await _sut.GetImage(imageId, CancellationToken.None);

        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("image/png");
    }
}

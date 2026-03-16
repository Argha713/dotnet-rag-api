// Argha - 2026-03-16 - #31 - Contract tests: verify IVisionService and IImageStore
// are Moq-mockable and expose the correct method signatures
using FluentAssertions;
using Moq;
using RagApi.Application.Interfaces;
using RagApi.Domain.Entities;

namespace RagApi.Tests.Unit.Application;

public class VisionInterfaceTests
{
    // Argha - 2026-03-16 - #31 - IVisionService: mock can describe an image and return a string
    [Fact]
    public async Task IVisionService_DescribeImageAsync_ReturnsDescription()
    {
        var mock = new Mock<IVisionService>();
        mock.Setup(s => s.DescribeImageAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("A diagram showing installation steps.");

        var result = await mock.Object.DescribeImageAsync(
            new byte[] { 0xFF, 0xD8 }, "image/jpeg", "installation manual");

        result.Should().Be("A diagram showing installation steps.");
    }

    // Argha - 2026-03-16 - #31 - IVisionService: IsEnabled property is mockable
    [Fact]
    public void IVisionService_IsEnabled_ReturnsFalseWhenNotConfigured()
    {
        var mock = new Mock<IVisionService>();
        mock.Setup(s => s.IsEnabled).Returns(false);

        mock.Object.IsEnabled.Should().BeFalse();
    }

    // Argha - 2026-03-16 - #31 - IImageStore: SaveAsync returns the image Id
    [Fact]
    public async Task IImageStore_SaveAsync_ReturnsImageId()
    {
        var image = new DocumentImage
        {
            DocumentId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            ContentType = "image/png",
            Data = new byte[] { 1, 2, 3 }
        };

        var mock = new Mock<IImageStore>();
        mock.Setup(s => s.SaveAsync(image, It.IsAny<CancellationToken>()))
            .ReturnsAsync(image.Id);

        var returnedId = await mock.Object.SaveAsync(image);

        returnedId.Should().Be(image.Id);
    }

    // Argha - 2026-03-16 - #31 - IImageStore: GetAsync returns null for unknown id
    [Fact]
    public async Task IImageStore_GetAsync_ReturnsNullForUnknownId()
    {
        var mock = new Mock<IImageStore>();
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentImage?)null);

        var result = await mock.Object.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }
}

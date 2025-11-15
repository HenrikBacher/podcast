using DrPodcast;
using FluentAssertions;

namespace DrPodcast.Tests;

public class PodcastHelpersTests
{
    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForNullInput()
    {
        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForEmptyList()
    {
        // Arrange
        var emptyList = new List<ImageAsset>();

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(emptyList);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldPreferPodcast1x1Image()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "16:9"),
            new("podcast-img", "podcast", "1:1"),
            new("other-img", "other", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToDefault1x1()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "1:1"),
            new("other-img", "other", "16:9")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/default-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToPodcastAnyRatio()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("podcast-img", "podcast", "16:9"),
            new("other-img", "other", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToDefaultAnyRatio()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "16:9"),
            new("other-img", "other", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/default-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldHandleCaseInsensitiveTarget()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("podcast-img", "PODCAST", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForEmptyId()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("", "podcast", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForNullId()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new(null, "podcast", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldConstructCorrectUrl()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new("test-image-id-12345", "podcast", "1:1")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/test-image-id-12345");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldHandleComplexPriority()
    {
        // Arrange - Test the full priority chain
        var imageAssets = new List<ImageAsset>
        {
            new("img1", "other", "1:1"),
            new("img2", "default", "16:9"),
            new("img3", "podcast", "16:9"),
            new("img4", "default", "4:3")
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        // Should pick podcast with 16:9 since no podcast 1:1 or default 1:1 exists
        result.Should().Be("https://asset.dr.dk/drlyd/images/img3");
    }
}

namespace DrPodcast.Tests;

public class PodcastHelpersTests
{
    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForNullInput()
    {
        var result = PodcastHelpers.GetImageUrlFromAssets(null);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForEmptyList()
    {
        var emptyList = new List<ImageAsset>();

        var result = PodcastHelpers.GetImageUrlFromAssets(emptyList);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldPreferPodcast1x1Image()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "16:9"),
            new("podcast-img", "podcast", "1:1"),
            new("other-img", "other", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToDefault1x1()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "1:1"),
            new("other-img", "other", "16:9")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().Be("https://asset.dr.dk/drlyd/images/default-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToPodcastAnyRatio()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("podcast-img", "podcast", "16:9"),
            new("other-img", "other", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFallbackToDefaultAnyRatio()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("default-img", "default", "16:9"),
            new("other-img", "other", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().Be("https://asset.dr.dk/drlyd/images/default-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldHandleCaseInsensitiveTarget()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("podcast-img", "PODCAST", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForEmptyId()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForNullId()
    {
        var imageAssets = new List<ImageAsset>
        {
            new(null, "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldConstructCorrectUrl()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("test-image-id-12345", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

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

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Should pick podcast with 16:9 since no podcast 1:1 or default 1:1 exists
        result.Should().Be("https://asset.dr.dk/drlyd/images/img3");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldRejectPathTraversal()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("../../etc/passwd", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldRejectBackslash()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("some\\path", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldRejectQueryString()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("image?evil=true", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldRejectFragment()
    {
        var imageAssets = new List<ImageAsset>
        {
            new("image#fragment", "podcast", "1:1")
        };

        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        result.Should().BeNull();
    }
}

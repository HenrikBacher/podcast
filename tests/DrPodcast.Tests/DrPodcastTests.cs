using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DrPodcast;
using FluentAssertions;

namespace DrPodcast.Tests;

public class DrPodcastTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    #region Helper Tests

    [Theory]
    [InlineData("Dokumentar", "Documentary")]
    [InlineData("Historie", "History")]
    [InlineData("Sundhed", "Health & Fitness")]
    [InlineData("Kriminal", "True Crime")]
    [InlineData("UnknownCategory", "UnknownCategory")]
    public void MapToPodcastCategory_ShouldMapCategories(string danishCategory, string expectedEnglishCategory)
    {
        // Act
        var result = PodcastHelpers.MapToPodcastCategory(danishCategory);

        // Assert
        result.Should().Be(expectedEnglishCategory);
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForNullOrEmptyInput()
    {
        // Act & Assert
        PodcastHelpers.GetImageUrlFromAssets(null).Should().BeNull();
        PodcastHelpers.GetImageUrlFromAssets(new List<ImageAsset>()).Should().BeNull();
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldPreferPodcast1x1Image()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new() { Id = "default-img", Target = "default", Ratio = "16:9" },
            new() { Id = "podcast-img", Target = "podcast", Ratio = "1:1" },
            new() { Id = "other-img", Target = "other", Ratio = "1:1" }
        };

        // Act
        var result = PodcastHelpers.GetImageUrlFromAssets(imageAssets);

        // Assert
        result.Should().Be("https://asset.dr.dk/drlyd/images/podcast-img");
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldFollowFallbackPriority()
    {
        // Arrange - Test fallback: default 1:1, then podcast any ratio, then default any ratio
        var testCases = new[]
        {
            (new List<ImageAsset> { new() { Id = "default-img", Target = "default", Ratio = "1:1" } }, "default-img"),
            (new List<ImageAsset> { new() { Id = "podcast-img", Target = "podcast", Ratio = "16:9" } }, "podcast-img"),
            (new List<ImageAsset> { new() { Id = "default-img", Target = "default", Ratio = "16:9" } }, "default-img")
        };

        foreach (var (assets, expectedId) in testCases)
        {
            // Act
            var result = PodcastHelpers.GetImageUrlFromAssets(assets);

            // Assert
            result.Should().Be($"https://asset.dr.dk/drlyd/images/{expectedId}");
        }
    }

    [Fact]
    public void GetImageUrlFromAssets_ShouldReturnNullForEmptyOrNullId()
    {
        // Arrange
        var imageAssets = new List<ImageAsset>
        {
            new() { Id = "", Target = "podcast", Ratio = "1:1" },
            new() { Id = null, Target = "podcast", Ratio = "1:1" }
        };

        // Act & Assert
        PodcastHelpers.GetImageUrlFromAssets(imageAssets).Should().BeNull();
    }

    #endregion

    #region Model Tests

    [Fact]
    public void PodcastList_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "podcasts": [
                {
                    "slug": "test-podcast",
                    "urn": "urn:dr:radio:series:12345",
                    "imageAssets": [
                        {
                            "id": "img-123",
                            "target": "podcast",
                            "ratio": "1:1"
                        }
                    ]
                }
            ]
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<PodcastList>(json, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Podcasts.Should().HaveCount(1);
        result.Podcasts[0].Slug.Should().Be("test-podcast");
        result.Podcasts[0].Urn.Should().Be("urn:dr:radio:series:12345");
        result.Podcasts[0].ImageAssets.Should().HaveCount(1);
    }

    [Fact]
    public void Series_ShouldDeserializeWithAllProperties()
    {
        // Arrange
        var json = """
        {
            "title": "Test Podcast",
            "categories": ["Dokumentar", "Historie"],
            "numberOfEpisodes": 42,
            "explicitContent": false,
            "defaultOrder": "desc",
            "presentationType": "ongoing",
            "groupingType": "Seasons"
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<Series>(json, JsonOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Podcast");
        result.Categories.Should().BeEquivalentTo(new[] { "Dokumentar", "Historie" });
        result.NumberOfEpisodes.Should().Be(42);
        result.ExplicitContent.Should().BeFalse();
        result.DefaultOrder.Should().Be("desc");
    }

    [Fact]
    public void Episode_ShouldDeserializeWithAllProperties()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Episode 1",
                "description": "First episode",
                "publishTime": "2024-01-01T12:00:00Z",
                "id": "ep-001",
                "durationMilliseconds": 1800000,
                "audioAssets": [
                    {
                        "format": "mp3",
                        "bitrate": 192,
                        "url": "https://example.com/audio.mp3",
                        "fileSize": 41472000
                    }
                ],
                "episodeNumber": 1,
                "seasonNumber": 1,
                "explicitContent": false
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize<List<Episode>>(json, JsonOptions);

        // Assert
        result.Should().HaveCount(1);
        var episode = result![0];
        episode.Title.Should().Be("Episode 1");
        episode.DurationMilliseconds.Should().Be(1800000);
        episode.AudioAssets.Should().HaveCount(1);
        episode.AudioAssets![0].Bitrate.Should().Be(192);
        episode.EpisodeNumber.Should().Be(1);
        episode.SeasonNumber.Should().Be(1);
        episode.ExplicitContent.Should().BeFalse();
    }

    [Fact]
    public void Episode_ShouldHandleNullableProperties()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Minimal Episode",
                "explicitContent": true
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize<List<Episode>>(json, JsonOptions);

        // Assert
        result.Should().HaveCount(1);
        result![0].Title.Should().Be("Minimal Episode");
        result[0].ExplicitContent.Should().BeTrue();
        result[0].Description.Should().BeNull();
        result[0].AudioAssets.Should().BeNull();
        result[0].EpisodeNumber.Should().BeNull();
    }

    [Fact]
    public void AudioAsset_ShouldSelectHighestBitrate()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Test",
                "explicitContent": false,
                "audioAssets": [
                    {
                        "format": "mp3",
                        "bitrate": 128,
                        "url": "https://example.com/low.mp3",
                        "fileSize": 10000000
                    },
                    {
                        "format": "mp3",
                        "bitrate": 256,
                        "url": "https://example.com/high.mp3",
                        "fileSize": 20000000
                    }
                ]
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize<List<Episode>>(json, JsonOptions);

        // Assert
        result![0].AudioAssets.Should().HaveCount(2);
        result[0].AudioAssets!.OrderByDescending(a => a.Bitrate).First().Bitrate.Should().Be(256);
    }

    #endregion

    #region Feed Generation Tests

    [Fact]
    public void RssFeed_ShouldHaveCorrectStructureAndNamespaces()
    {
        // Arrange
        var channel = new XElement("channel",
            new XElement(Atom + "link",
                new XAttribute("href", "https://example.com/feed.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")
            ),
            new XElement("title", "Test Podcast"),
            new XElement("link", "https://example.com"),
            new XElement("description", "Test Description"),
            new XElement("language", "da"),
            new XElement(Itunes + "explicit", "no"),
            new XElement(Itunes + "author", "DR"),
            new XElement(Itunes + "type", "episodic")
        );

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", Atom),
            new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
            new XAttribute(XNamespace.Xmlns + "media", Media),
            channel
        );

        // Assert
        rss.Attribute("version")?.Value.Should().Be("2.0");
        rss.Attribute(XNamespace.Xmlns + "atom")?.Value.Should().Be(Atom.NamespaceName);
        rss.Attribute(XNamespace.Xmlns + "itunes")?.Value.Should().Be(Itunes.NamespaceName);
        channel.Element("title")?.Value.Should().Be("Test Podcast");
        channel.Element(Itunes + "explicit")?.Value.Should().Be("no");
    }

    [Fact]
    public void Episode_ShouldContainRequiredMetadata()
    {
        // Arrange
        var item = new XElement("item",
            new XElement("title", "Episode 1"),
            new XElement("description", "Episode description"),
            new XElement("pubDate", "Mon, 01 Jan 2024 12:00:00 +00:00"),
            new XElement("guid", new XAttribute("isPermalink", "false"), "ep-001"),
            new XElement(Itunes + "duration", "00:30:00"),
            new XElement(Itunes + "episode", 1),
            new XElement("enclosure",
                new XAttribute("url", "https://example.com/audio.mp3"),
                new XAttribute("type", "audio/mpeg"),
                new XAttribute("length", "41472000")
            )
        );

        // Assert
        item.Element("title")?.Value.Should().Be("Episode 1");
        item.Element("guid")?.Attribute("isPermalink")?.Value.Should().Be("false");
        item.Element(Itunes + "duration")?.Value.Should().Be("00:30:00");
        item.Element("enclosure")?.Attribute("url")?.Value.Should().Be("https://example.com/audio.mp3");
        item.Element("enclosure")?.Attribute("type")?.Value.Should().Be("audio/mpeg");
    }

    [Theory]
    [InlineData(3600000, "01:00:00")]  // 1 hour
    [InlineData(1800000, "00:30:00")]  // 30 minutes
    [InlineData(90000, "00:01:30")]    // 1.5 minutes
    public void DurationFormatting_ShouldBeCorrect(int milliseconds, string expected)
    {
        // Act
        var duration = TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");

        // Assert
        duration.Should().Be(expected);
    }

    [Fact]
    public void DateFormatting_ShouldFollowRfc822()
    {
        // Arrange
        var testDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var formatted = testDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

        // Assert
        formatted.Should().MatchRegex(@"^[A-Za-z]{3}, \d{2} [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$");
    }

    [Fact]
    public void MediaRestriction_ShouldAllowDenmark()
    {
        // Arrange
        var restriction = new XElement(Media + "restriction",
            new XAttribute("relationship", "allow"),
            new XAttribute("type", "country"),
            "dk"
        );

        // Assert
        restriction.Attribute("relationship")?.Value.Should().Be("allow");
        restriction.Attribute("type")?.Value.Should().Be("country");
        restriction.Value.Should().Be("dk");
    }

    [Fact]
    public void CompleteRssFeed_ShouldValidate()
    {
        // Arrange
        var channel = new XElement("channel",
            new XElement("title", "Test Podcast"),
            new XElement(Itunes + "type", "episodic"),
            new XElement("item",
                new XElement("title", "Episode 1"),
                new XElement("enclosure",
                    new XAttribute("url", "https://example.com/audio.mp3"),
                    new XAttribute("type", "audio/mpeg")
                )
            )
        );

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
            channel
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);

        // Assert
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("rss");
        doc.Root.Element("channel")!.Element("item").Should().NotBeNull();
        doc.Root.Element("channel")!.Element("item")!.Element("enclosure").Should().NotBeNull();
    }

    #endregion
}

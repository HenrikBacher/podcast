using System.Text.Json;
using DrPodcast;
using FluentAssertions;

namespace DrPodcast.Tests;

public class PodcastModelsTests
{
    [Fact]
    public void PodcastList_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
            "podcasts": [
                {
                    "slug": "test-podcast",
                    "urn": "urn:dr:radio:series:12345"
                }
            ]
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.PodcastList);

        // Assert
        result.Should().NotBeNull();
        result!.Podcasts.Should().HaveCount(1);
        result.Podcasts[0].Slug.Should().Be("test-podcast");
        result.Podcasts[0].Urn.Should().Be("urn:dr:radio:series:12345");
    }

    [Fact]
    public void Podcast_ShouldDeserializeWithImageAssets()
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
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.PodcastList);

        // Assert
        result.Should().NotBeNull();
        result!.Podcasts[0].ImageAssets.Should().NotBeNull();
        result.Podcasts[0].ImageAssets!.Should().HaveCount(1);
        result.Podcasts[0].ImageAssets![0].Id.Should().Be("img-123");
        result.Podcasts[0].ImageAssets![0].Target.Should().Be("podcast");
        result.Podcasts[0].ImageAssets![0].Ratio.Should().Be("1:1");
    }

    [Fact]
    public void Series_ShouldDeserializeCompleteObject()
    {
        // Arrange
        var json = """
        {
            "type": "podcast",
            "categories": ["Dokumentar", "Historie"],
            "numberOfEpisodes": 42,
            "numberOfSeries": 3,
            "numberOfSeasons": 2,
            "presentationType": "ongoing",
            "groupingType": "Seasons",
            "latestEpisodeStartTime": "2024-01-15T10:00:00Z",
            "presentationUrl": "https://www.dr.dk/lyd/special-radio/test-podcast",
            "explicitContent": false,
            "defaultOrder": "desc",
            "id": "12345",
            "slug": "test-podcast",
            "title": "Test Podcast",
            "punchline": "A great test podcast",
            "description": "This is a detailed description of the test podcast.",
            "imageAssets": [
                {
                    "id": "img-456",
                    "target": "default",
                    "ratio": "16:9"
                }
            ]
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.Series);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be("podcast");
        result.Categories.Should().BeEquivalentTo(new[] { "Dokumentar", "Historie" });
        result.NumberOfEpisodes.Should().Be(42);
        result.NumberOfSeries.Should().Be(3);
        result.NumberOfSeasons.Should().Be(2);
        result.PresentationType.Should().Be("ongoing");
        result.GroupingType.Should().Be("Seasons");
        result.LatestEpisodeStartTime.Should().Be("2024-01-15T10:00:00Z");
        result.PresentationUrl.Should().Be("https://www.dr.dk/lyd/special-radio/test-podcast");
        result.ExplicitContent.Should().BeFalse();
        result.DefaultOrder.Should().Be("desc");
        result.Id.Should().Be("12345");
        result.Slug.Should().Be("test-podcast");
        result.Title.Should().Be("Test Podcast");
        result.Punchline.Should().Be("A great test podcast");
        result.Description.Should().Be("This is a detailed description of the test podcast.");
        result.ImageAssets.Should().HaveCount(1);
    }

    [Fact]
    public void Episode_ShouldDeserializeWithAllProperties()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Episode 1",
                "description": "First episode description",
                "publishTime": "2024-01-01T12:00:00Z",
                "id": "ep-001",
                "presentationUrl": "https://www.dr.dk/lyd/episode-1",
                "durationMilliseconds": 1800000,
                "audioAssets": [
                    {
                        "format": "mp3",
                        "bitrate": 192,
                        "url": "https://example.com/audio.mp3",
                        "fileSize": 41472000
                    }
                ],
                "categories": ["Dokumentar"],
                "imageAssets": [
                    {
                        "id": "ep-img-001",
                        "target": "podcast",
                        "ratio": "1:1"
                    }
                ],
                "episodeNumber": 1,
                "seasonNumber": 1,
                "explicitContent": false
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        var episode = result![0];
        episode.Title.Should().Be("Episode 1");
        episode.Description.Should().Be("First episode description");
        episode.PublishTime.Should().Be("2024-01-01T12:00:00Z");
        episode.Id.Should().Be("ep-001");
        episode.PresentationUrl.Should().Be("https://www.dr.dk/lyd/episode-1");
        episode.DurationMilliseconds.Should().Be(1800000);
        episode.AudioAssets.Should().HaveCount(1);
        episode.AudioAssets![0].Format.Should().Be("mp3");
        episode.AudioAssets[0].Bitrate.Should().Be(192);
        episode.AudioAssets[0].Url.Should().Be("https://example.com/audio.mp3");
        episode.AudioAssets[0].FileSize.Should().Be(41472000);
        episode.Categories.Should().BeEquivalentTo(new[] { "Dokumentar" });
        episode.ImageAssets.Should().HaveCount(1);
        episode.EpisodeNumber.Should().Be(1);
        episode.SeasonNumber.Should().Be(1);
        episode.ExplicitContent.Should().BeFalse();
    }

    [Fact]
    public void Episode_ShouldHandleExplicitContent()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Explicit Episode",
                "explicitContent": true
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        // Assert
        result.Should().NotBeNull();
        result![0].ExplicitContent.Should().BeTrue();
    }

    [Fact]
    public void Episode_ShouldHandleNullableProperties()
    {
        // Arrange
        var json = """
        [
            {
                "title": "Minimal Episode",
                "explicitContent": false
            }
        ]
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        // Assert
        result.Should().NotBeNull();
        result![0].Title.Should().Be("Minimal Episode");
        result[0].Description.Should().BeNull();
        result[0].AudioAssets.Should().BeNull();
        result[0].EpisodeNumber.Should().BeNull();
        result[0].SeasonNumber.Should().BeNull();
    }

    [Fact]
    public void AudioAsset_ShouldDeserializeCorrectly()
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
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        // Assert
        result.Should().NotBeNull();
        result![0].AudioAssets.Should().HaveCount(2);
        result[0].AudioAssets![0].Bitrate.Should().Be(128);
        result[0].AudioAssets![1].Bitrate.Should().Be(256);
    }

    [Fact]
    public void Channel_ShouldDeserializeWithOwner()
    {
        // Arrange
        var channelModel = new Channel
        {
            Title = "Test Channel",
            Link = "https://example.com",
            Description = "Test Description",
            Language = "da",
            Copyright = "DR",
            LastBuildDate = "Mon, 01 Jan 2024 12:00:00 +00:00",
            Explicit = "no",
            Author = "DR",
            Block = "yes",
            Owner = new ChannelOwner { Email = "test@dr.dk", Name = "DR Test" },
            NewFeedUrl = "https://example.com/feed.xml",
            Image = "https://example.com/image.jpg"
        };

        // Assert
        channelModel.Owner.Should().NotBeNull();
        channelModel.Owner!.Email.Should().Be("test@dr.dk");
        channelModel.Owner.Name.Should().Be("DR Test");
    }

    [Fact]
    public void ImageAsset_ShouldHandleAllProperties()
    {
        // Arrange
        var imageAsset = new ImageAsset
        {
            Id = "img-123",
            Target = "podcast",
            Ratio = "1:1"
        };

        // Assert
        imageAsset.Id.Should().Be("img-123");
        imageAsset.Target.Should().Be("podcast");
        imageAsset.Ratio.Should().Be("1:1");
    }

    [Fact]
    public void Series_ShouldHandleEmptyCategories()
    {
        // Arrange
        var json = """
        {
            "numberOfEpisodes": 0,
            "numberOfSeries": 0,
            "numberOfSeasons": 0,
            "explicitContent": false,
            "categories": []
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.Series);

        // Assert
        result.Should().NotBeNull();
        result!.Categories.Should().NotBeNull();
        result.Categories.Should().BeEmpty();
    }
}

namespace DrPodcast.Tests;

public class PodcastModelsTests
{
    [Fact]
    public void PodcastList_ShouldDeserializeFromJson()
    {
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

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.PodcastList);

        result.Should().BeEquivalentTo(new PodcastList
        {
            Podcasts = [new Podcast("test-podcast", "urn:dr:radio:series:12345", null)]
        });
    }

    [Fact]
    public void Podcast_ShouldDeserializeWithImageAssets()
    {
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

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.PodcastList);

        result.Should().BeEquivalentTo(new PodcastList
        {
            Podcasts = [new Podcast("test-podcast", "urn:dr:radio:series:12345",
                [new ImageAsset("img-123", "podcast", "1:1")])]
        });
    }

    [Fact]
    public void Series_ShouldDeserializeCompleteObject()
    {
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

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.Series);

        result.Should().BeEquivalentTo(new Series(
            Categories: ["Dokumentar", "Historie"],
            NumberOfSeries: 3,
            PresentationType: "ongoing",
            LatestEpisodeStartTime: "2024-01-15T10:00:00Z",
            PresentationUrl: "https://www.dr.dk/lyd/special-radio/test-podcast",
            ExplicitContent: false,
            DefaultOrder: "desc",
            Title: "Test Podcast",
            Punchline: "A great test podcast",
            Description: "This is a detailed description of the test podcast.",
            ImageAssets: [new ImageAsset("img-456", "default", "16:9")]
        ));
    }

    [Fact]
    public void Episode_ShouldDeserializeWithAllProperties()
    {
        var json = """
        [
            {
                "title": "Episode 1",
                "description": "First episode description",
                "publishTime": "2024-01-01T12:00:00Z",
                "startTime": "2024-01-01T12:00:00Z",
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

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        result.Should().BeEquivalentTo(new[]
        {
            new Episode(
                Title: "Episode 1",
                Description: "First episode description",
                PublishTime: "2024-01-01T12:00:00Z",
                StartTime: "2024-01-01T12:00:00Z",
                Id: "ep-001",
                PresentationUrl: "https://www.dr.dk/lyd/episode-1",
                DurationMilliseconds: 1800000,
                AudioAssets: [new AudioAsset("mp3", 192, "https://example.com/audio.mp3", 41472000)],
                Categories: ["Dokumentar"],
                ImageAssets: [new ImageAsset("ep-img-001", "podcast", "1:1")],
                EpisodeNumber: 1,
                SeasonNumber: 1,
                ExplicitContent: false,
                Order: null
            )
        });
    }

    [Fact]
    public void Episode_ShouldHandleExplicitContent()
    {
        var json = """
        [
            {
                "title": "Explicit Episode",
                "explicitContent": true
            }
        ]
        """;

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        result.Should().NotBeNull();
        result![0].ExplicitContent.Should().BeTrue();
    }

    [Fact]
    public void Episode_ShouldHandleNullableProperties()
    {
        var json = """
        [
            {
                "title": "Minimal Episode",
                "explicitContent": false
            }
        ]
        """;

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        result.Should().BeEquivalentTo(new[]
        {
            new Episode(
                Title: "Minimal Episode",
                Description: null,
                PublishTime: null,
                StartTime: null,
                Id: null,
                PresentationUrl: null,
                DurationMilliseconds: null,
                AudioAssets: null,
                Categories: null,
                ImageAssets: null,
                EpisodeNumber: null,
                SeasonNumber: null,
                ExplicitContent: false,
                Order: null
            )
        });
    }

    [Fact]
    public void AudioAsset_ShouldDeserializeCorrectly()
    {
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

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.ListEpisode);

        result.Should().NotBeNull();
        result![0].AudioAssets.Should().BeEquivalentTo(new[]
        {
            new AudioAsset("mp3", 128, "https://example.com/low.mp3", 10000000),
            new AudioAsset("mp3", 256, "https://example.com/high.mp3", 20000000),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ImageAsset_ShouldHandleAllProperties()
    {
        var imageAsset = new ImageAsset("img-123", "podcast", "1:1");

        imageAsset.Should().BeEquivalentTo(new ImageAsset("img-123", "podcast", "1:1"));
    }

    [Fact]
    public void Series_ShouldHandleEmptyCategories()
    {
        var json = """
        {
            "numberOfEpisodes": 0,
            "numberOfSeries": 0,
            "numberOfSeasons": 0,
            "explicitContent": false,
            "categories": []
        }
        """;

        var result = JsonSerializer.Deserialize(json, PodcastJsonContext.Default.Series);

        result.Should().NotBeNull();
        result!.Categories.Should().BeEmpty();
    }
}

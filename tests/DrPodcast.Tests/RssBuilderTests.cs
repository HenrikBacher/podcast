using static DrPodcast.Tests.TestData;

namespace DrPodcast.Tests;

public class RssBuilderTests
{
    #region FormatRfc822

    [Fact]
    public void FormatRfc822_ShouldProduceCompactTimezoneOffset()
    {
        var dt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = RssBuilder.FormatRfc822(dt);

        result.Should().EndWith("+0000");
        result.Should().Contain("15 Jan 2024 10:30:00");
    }

    [Fact]
    public void FormatRfc822_ShouldNotContainColonInTimezone()
    {
        var dt = new DateTime(2024, 3, 15, 14, 0, 0, DateTimeKind.Utc);

        var result = RssBuilder.FormatRfc822(dt);

        // RFC 822 requires +0000 not +00:00
        result.Should().NotContain("+00:00");
        result.Should().Contain("+0000");
    }

    #endregion

    #region DetermineItunesType

    [Fact]
    public void DetermineItunesType_Show_ReturnsSerial()
    {
        var series = CreateSeries(presentationType: "Show");
        RssBuilder.DetermineItunesType(series).Should().Be("serial");
    }

    [Fact]
    public void DetermineItunesType_NonShow_ReturnsEpisodic()
    {
        var series = CreateSeries(presentationType: "podcast");
        RssBuilder.DetermineItunesType(series).Should().Be("episodic");
    }

    [Fact]
    public void DetermineItunesType_NullSeries_ReturnsEpisodic()
    {
        RssBuilder.DetermineItunesType(null).Should().Be("episodic");
    }

    #endregion

    #region BuildRssFeed - Episode Sorting

    [Fact]
    public void BuildRssFeed_SeasonalAsc_SortsLatestSeasonFirstThenEpisodesAscending()
    {
        var series = CreateSeries(numberOfSeries: 2, defaultOrder: "Asc");
        var episodes = new List<Episode>
        {
            CreateEpisode("S1E1", seasonNumber: 1, order: 1),
            CreateEpisode("S1E2", seasonNumber: 1, order: 2),
            CreateEpisode("S2E1", seasonNumber: 2, order: 1),
            CreateEpisode("S2E2", seasonNumber: 2, order: 2),
        };
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("S2E1", "S2E2", "S1E1", "S1E2");
    }

    [Fact]
    public void BuildRssFeed_SeasonalDesc_SortsLatestSeasonFirstThenEpisodesDescending()
    {
        var series = CreateSeries(numberOfSeries: 2, defaultOrder: "Desc");
        var episodes = new List<Episode>
        {
            CreateEpisode("S1E1", seasonNumber: 1, order: 1),
            CreateEpisode("S1E2", seasonNumber: 1, order: 2),
            CreateEpisode("S2E1", seasonNumber: 2, order: 1),
            CreateEpisode("S2E2", seasonNumber: 2, order: 2),
        };
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("S2E2", "S2E1", "S1E2", "S1E1");
    }

    [Fact]
    public void BuildRssFeed_NonSeasonalAsc_SortsEpisodesAscending()
    {
        var series = CreateSeries(numberOfSeries: 0, defaultOrder: "Asc");
        var episodes = new List<Episode>
        {
            CreateEpisode("Ep3", order: 3),
            CreateEpisode("Ep1", order: 1),
            CreateEpisode("Ep2", order: 2),
        };
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("Ep1", "Ep2", "Ep3");
    }

    [Fact]
    public void BuildRssFeed_NonSeasonalDesc_SortsEpisodesDescending()
    {
        var series = CreateSeries(numberOfSeries: 0, defaultOrder: "Desc");
        var episodes = new List<Episode>
        {
            CreateEpisode("Ep1", order: 1),
            CreateEpisode("Ep3", order: 3),
            CreateEpisode("Ep2", order: 2),
        };
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("Ep3", "Ep2", "Ep1");
    }

    [Fact]
    public void BuildRssFeed_SeasonCountOnlyInNumberOfSeasons_SortsLatestSeasonFirst()
    {
        // DR reports some series' seasons as numberOfSeasons rather than numberOfSeries ("laegens-bord"),
        // and those were sorted as if they had no seasons: season 1 first.
        var series = CreateSeries(numberOfSeasons: 2, defaultOrder: "Asc");
        var episodes = new List<Episode>
        {
            CreateEpisode("S1E1", seasonNumber: 1, order: 100001),
            CreateEpisode("S1E2", seasonNumber: 1, order: 100002),
            CreateEpisode("S2E1", seasonNumber: 2, order: 200001),
        };
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("S2E1", "S1E1", "S1E2");
    }

    [Fact]
    public void BuildRssFeed_LeavesOutEpisodesWithoutADownloadableFile()
    {
        // DR offers some episodes only as an HLS stream; in a podcast app they'd be unplayable entries.
        var episodes = new List<Episode>
        {
            CreateEpisode("Has mp3", order: 2, audioAssets: [new AudioAsset("mp3", 192, "https://example.com/a.mp3", 1)]),
            CreateEpisode("Stream only", order: 1, audioAssets: [new AudioAsset("HLS", 0, "https://example.com/a.m3u8", null)]),
        };

        var rss = RssBuilder.BuildRssFeed(CreateSeries(), episodes, new Podcast("test", "urn:test", null), "https://example.com");

        rss.Descendants("item").Select(i => i.Element("title")?.Value).Should().Equal("Has mp3");
    }

    [Fact]
    public void BuildRssFeed_DoesNotPutItunesSeasonOnTheChannel()
    {
        // itunes:season is an episode-level tag.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(CreateSeries(numberOfSeries: 3), [], podcast, "https://example.com");

        rss.Element("channel")!.Element(itunes + "season").Should().BeNull();
    }

    [Fact]
    public void BuildRssFeed_NullEpisodes_ProducesEmptyChannel()
    {
        var series = CreateSeries();
        var podcast = new Podcast("test", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, null, podcast, "https://example.com");

        rss.Descendants("item").Should().BeEmpty();
    }

    [Fact]
    public void BuildRssFeed_IncludesAtomSelfLink()
    {
        var series = CreateSeries();
        var podcast = new Podcast("my-show", "urn:test", null);

        var rss = RssBuilder.BuildRssFeed(series, [], podcast, "https://feeds.example.com");

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var link = rss.Descendants(atom + "link").FirstOrDefault();
        link.Should().NotBeNull();
        link!.Attribute("href")!.Value.Should().Be("https://feeds.example.com/feeds/my-show.xml");
    }

    #endregion

    #region BuildEpisodeItem

    [Fact]
    public void BuildEpisodeItem_SelectsHighestBitrateMp3()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets:
        [
            new AudioAsset("mp3", 128, "https://example.com/low.mp3", 10000),
            new AudioAsset("mp3", 256, "https://example.com/high.mp3", 20000),
        ]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        var enclosure = item.Element("enclosure");
        enclosure.Should().NotBeNull();
        enclosure!.Attribute("url")!.Value.Should().Be("https://example.com/high.mp3");
        enclosure.Attribute("type")!.Value.Should().Be("audio/mpeg");
    }

    [Fact]
    public void BuildEpisodeItem_NoAudioAssets_NoEnclosure()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test") with { AudioAssets = null };

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element("enclosure").Should().BeNull();
    }

    [Fact]
    public void BuildEpisodeItem_UsesChannelImageAsFallback()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", imageAssets: null);

        var item = RssBuilder.BuildEpisodeItem(episode, "https://channel-image.jpg", itunes);

        var image = item.Element(itunes + "image");
        image.Should().NotBeNull();
        image!.Attribute("href")!.Value.Should().Be("https://channel-image.jpg");
    }

    [Fact]
    public void BuildEpisodeItem_IncludesFileSize()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets:
        [
            new AudioAsset("mp3", 192, "https://example.com/audio.mp3", 41472000),
        ]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        var enclosure = item.Element("enclosure");
        enclosure!.Attribute("length")!.Value.Should().Be("41472000");
    }

    [Theory]
    [InlineData(3_723_000, "3723")]
    [InlineData(90_000_000, "90000")] // 25 hours, which an hh:mm:ss format wraps to 01:00:00
    public void BuildEpisodeItem_WritesDurationInWholeSeconds(int durationMilliseconds, string expected)
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test") with { DurationMilliseconds = durationMilliseconds };

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element(itunes + "duration")!.Value.Should().Be(expected);
    }

    [Fact]
    public void BuildEpisodeItem_UnknownDuration_OmitsTheElement()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test") with { DurationMilliseconds = null };

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element(itunes + "duration").Should().BeNull();
    }

    [Fact]
    public void BuildEpisodeItem_Guid_IsMarkedAsNotAPermalink()
    {
        // RSS 2.0 spells it isPermaLink. XML is case-sensitive, so another spelling is ignored and the
        // guid defaults to being a permalink, which an episode id isn't.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var guid = RssBuilder.BuildEpisodeItem(CreateEpisode("Test"), null, itunes).Element("guid")!;

        guid.Attribute("isPermaLink")!.Value.Should().Be("false");
        guid.Attributes().Should().ContainSingle();
    }

    [Fact]
    public void BuildEpisodeItem_UnknownFileSize_WritesZeroLength()
    {
        // length is a required enclosure attribute.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets: [new AudioAsset("mp3", 192, "https://example.com/audio.mp3", null)]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element("enclosure")!.Attribute("length")!.Value.Should().Be("0");
    }

    [Fact]
    public void BuildEpisodeItem_Categories_UsePlainRssCategory()
    {
        // itunes:category is only valid on the channel.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", categories: ["Historie", ""]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Elements("category").Select(c => c.Value).Should().Equal("Historie");
        item.Elements(itunes + "category").Should().BeEmpty();
    }

    [Fact]
    public void BuildEpisodeItem_PrefersStartTimeOverPublishTime()
    {
        // DR re-indexes old content by updating publishTime; startTime is stable.
        // The feed must use startTime so podcatchers don't treat re-indexed episodes as new.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = new Episode(
            Title: "Old episode", Description: null,
            PublishTime: "2026-04-13T23:55:00+02:00",
            StartTime: "2021-11-18T04:00:00+01:00",
            Id: "ep-old", PresentationUrl: null, DurationMilliseconds: 1000,
            AudioAssets: null, Categories: null, ImageAssets: null,
            EpisodeNumber: null, SeasonNumber: null, ExplicitContent: false, Order: null);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element("pubDate")!.Value.Should().Contain("2021").And.Contain("Nov");
    }

    [Fact]
    public void BuildEpisodeItem_FallsBackToPublishTimeWhenStartTimeMissing()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = new Episode(
            Title: "Test", Description: null,
            PublishTime: "2024-01-01T00:00:00Z",
            StartTime: null,
            Id: "ep-1", PresentationUrl: null, DurationMilliseconds: 1000,
            AudioAssets: null, Categories: null, ImageAssets: null,
            EpisodeNumber: null, SeasonNumber: null, ExplicitContent: false, Order: null);

        var item = RssBuilder.BuildEpisodeItem(episode, null, itunes);

        item.Element("pubDate")!.Value.Should().Contain("2024").And.Contain("Jan");
    }

    #endregion

    #region FeedUrl and metadata

    [Fact]
    public void FeedUrl_KeepsTheShapeExistingSubscriptionsUse()
    {
        RssBuilder.FeedUrl("https://podcasts.example.dk/", "genstart").Should().Be("https://podcasts.example.dk/feeds/genstart.xml");
        RssBuilder.FeedUrl("https://podcasts.example.dk", "genstart").Should().Be("https://podcasts.example.dk/feeds/genstart.xml");
    }

    [Fact]
    public void BuildFeedMetadata_CarriesWhatTheWebsiteShows()
    {
        var series = CreateSeries(title: "Bakspejl") with
        {
            Punchline = "Afgørende nedslag i historien",
            Categories = ["Dokumentar", "", "Historie"],
            LatestEpisodeStartTime = "2026-10-05T05:00:00+02:00",
        };

        var metadata = RssBuilder.BuildFeedMetadata(new Podcast("bakspejl", "urn:test", null), series,
            CreateEpisode("LA Riots 3:4") with { StartTime = "2026-10-05T05:00:00+02:00" });

        metadata.Should().Be(new FeedMetadata("bakspejl", "Bakspejl", null, "Afgørende nedslag i historien", "Dokumentar|Historie", null,
            "LA Riots 3:4", new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc), Description: "Test description"));
    }

    #endregion

    #region Serialize

    [Fact]
    public void Serialize_WritesUtf8WithoutBomAfterAnXmlDeclaration()
    {
        // Feeds on disk are compared byte for byte with this output before being rewritten, so a
        // format change would rewrite every feed once.
        var bytes = RssBuilder.Serialize(new XElement("rss", new XElement("channel", "æøå")));

        System.Text.Encoding.UTF8.GetString(bytes.Span)
            .Should().Be("""<?xml version="1.0" encoding="utf-8" standalone="yes"?><rss><channel>æøå</channel></rss>""");
    }

    #endregion
}

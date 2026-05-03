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

        var (rss, _) = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

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

        var (rss, _) = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

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

        var (rss, _) = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

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

        var (rss, _) = RssBuilder.BuildRssFeed(series, episodes, podcast, "https://example.com");

        var items = rss.Descendants("item").Select(i => i.Element("title")?.Value).ToList();
        items.Should().Equal("Ep3", "Ep2", "Ep1");
    }

    [Fact]
    public void BuildRssFeed_NullEpisodes_ProducesEmptyChannel()
    {
        var series = CreateSeries();
        var podcast = new Podcast("test", "urn:test", null);

        var (rss, _) = RssBuilder.BuildRssFeed(series, null, podcast, "https://example.com");

        rss.Descendants("item").Should().BeEmpty();
    }

    [Fact]
    public void BuildRssFeed_IncludesAtomSelfLink()
    {
        var series = CreateSeries();
        var podcast = new Podcast("my-show", "urn:test", null);

        var (rss, _) = RssBuilder.BuildRssFeed(series, [], podcast, "https://feeds.example.com");

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

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

        var enclosure = item.Element("enclosure");
        enclosure.Should().NotBeNull();
        enclosure!.Attribute("url")!.Value.Should().Be("https://example.com/high.mp3");
        enclosure.Attribute("type")!.Value.Should().Be("audio/mpeg");
    }

    [Fact]
    public void BuildEpisodeItem_PreferMp4_SelectsMp4OverMp3()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets:
        [
            new AudioAsset("mp3", 256, "https://example.com/high.mp3", 20000),
            new AudioAsset("mp4", 128, "https://example.com/audio.mp4", 15000),
        ]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: true, itunes);

        var enclosure = item.Element("enclosure");
        enclosure.Should().NotBeNull();
        enclosure!.Attribute("type")!.Value.Should().Be("audio/mp4");
    }

    [Fact]
    public void BuildEpisodeItem_PreferMp4_FallsBackToMp3WhenNoMp4()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets:
        [
            new AudioAsset("mp3", 192, "https://example.com/audio.mp3", 15000),
        ]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: true, itunes);

        var enclosure = item.Element("enclosure");
        enclosure.Should().NotBeNull();
        enclosure!.Attribute("url")!.Value.Should().Be("https://example.com/audio.mp3");
    }

    [Fact]
    public void BuildEpisodeItem_PreferMp4_DrAsset_RewritesToProxyUrlWithM4aSuffix()
    {
        // The proxy URL must end in .m4a so older podcatchers that infer codec from the
        // URL extension (AntennaPod < 2.x, Podkicker, etc.) accept the enclosure.
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        const string upstream = "https://api.dr.dk/radio/v1/assetlinks/urn:dr:radio:episode:abc123/aabbccddeeff112233445566778899aabbccddeeff112233445566778899aabb";
        var episode = CreateEpisode("Test", audioAssets:
        [
            new AudioAsset("m4a", 128, upstream, 15000),
        ]);

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: true, itunes);

        var enclosure = item.Element("enclosure")!;
        enclosure.Attribute("url")!.Value.Should().Be(
            "https://example.com/proxy/audio/abc123/aabbccddeeff112233445566778899aabbccddeeff112233445566778899aabb.m4a");
        enclosure.Attribute("type")!.Value.Should().Be("audio/mp4");
    }

    [Fact]
    public void BuildEpisodeItem_NoAudioAssets_NoEnclosure()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", audioAssets: null);

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

        item.Element("enclosure").Should().BeNull();
    }

    [Fact]
    public void BuildEpisodeItem_UsesChannelImageAsFallback()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = CreateEpisode("Test", imageAssets: null);

        var item = RssBuilder.BuildEpisodeItem(episode, "https://channel-image.jpg", "https://example.com", preferMp4: false, itunes);

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

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

        var enclosure = item.Element("enclosure");
        enclosure!.Attribute("length")!.Value.Should().Be("41472000");
    }

    [Fact]
    public void BuildEpisodeItem_FormatsDuration()
    {
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        var episode = new Episode(
            Title: "Test", Description: null, PublishTime: "2024-01-01T00:00:00Z",
            StartTime: null,
            Id: "ep-1", PresentationUrl: null, DurationMilliseconds: 3723000,
            AudioAssets: null, Categories: null, ImageAssets: null,
            EpisodeNumber: null, SeasonNumber: null, ExplicitContent: false, Order: null);

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

        item.Element(itunes + "duration")!.Value.Should().Be("01:02:03");
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

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

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

        var item = RssBuilder.BuildEpisodeItem(episode, null, "https://example.com", preferMp4: false, itunes);

        item.Element("pubDate")!.Value.Should().Contain("2024").And.Contain("Jan");
    }

    #endregion

    #region Helpers

    private static Series CreateSeries(
        string? presentationType = null,
        int numberOfSeries = 0,
        string? defaultOrder = null,
        string? title = "Test Series") => new(
        Categories: null,
        NumberOfSeries: numberOfSeries,
        PresentationType: presentationType,
        LatestEpisodeStartTime: null,
        PresentationUrl: null,
        ExplicitContent: false,
        DefaultOrder: defaultOrder,
        Title: title,
        Punchline: null,
        Description: "Test description",
        ImageAssets: null
    );

    private static Episode CreateEpisode(
        string title,
        int? seasonNumber = null,
        long? order = null,
        List<AudioAsset>? audioAssets = null,
        List<ImageAsset>? imageAssets = null)
    {
        return new Episode(
            Title: title,
            Description: null,
            PublishTime: "2024-01-01T00:00:00Z",
            StartTime: "2024-01-01T00:00:00Z",
            Id: $"ep-{title}",
            PresentationUrl: null,
            DurationMilliseconds: 60000,
            AudioAssets: audioAssets,
            Categories: null,
            ImageAssets: imageAssets,
            EpisodeNumber: null,
            SeasonNumber: seasonNumber,
            ExplicitContent: false,
            Order: order
        );
    }

    #endregion
}

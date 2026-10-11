using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrPodcast.Tests;

/// <summary>Whole <see cref="FeedGenerationService.GenerateFeedsAsync"/> runs against an in-memory DR API.</summary>
public class FeedGenerationRunTests : IDisposable
{
    private const string UrnA = "urn:dr:radio:series:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string UrnB = "urn:dr:radio:series:bbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "drpodcast-run-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDrApi _api = new();
    private readonly GeneratorConfig _config;
    private readonly PodcastList _podcasts = new() { Podcasts = [new("alpha", UrnA, null), new("bravo", UrnB, null)] };

    public FeedGenerationRunTests()
    {
        _config = new GeneratorConfig(OutputDir: Path.Combine(_root, "output"), SiteSourceDir: Path.Combine(_root, "site"));
        Directory.CreateDirectory(_config.SiteSourceDir);
        File.WriteAllText(Path.Combine(_config.SiteSourceDir, "index.html"), "<ul><!-- BEGIN_FEEDS --><!-- END_FEEDS --></ul>");
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 2), FakeDrApi.Episodes(2));
        _api.Set(UrnB, FakeDrApi.SeriesWith("Bravo", latestEpisode: 2), FakeDrApi.Episodes(2));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // A new instance stands in for a process restart: the files on disk persist, in-memory state doesn't.
    private FeedGenerationService NewService(ILogger<FeedGenerationService>? logger = null, TimeSpan? httpTimeout = null) =>
        new(new DrApiClient(new StubHttpClientFactory(_api, httpTimeout), NullLogger<DrApiClient>.Instance),
            logger ?? NullLogger<FeedGenerationService>.Instance);

    private string FeedPath(string slug) => Path.Combine(_config.FeedsDir, $"{slug}.xml");

    private string Listing() => File.ReadAllText(Path.Combine(_config.FullSiteDir, "index.html"));

    [Fact]
    public async Task PodcastFailingOnTheStartupRun_IsListedAgainOnceItRecovers()
    {
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        // Restart with the feeds still on disk. Bravo fails on the startup run, so the new process
        // has no metadata for it and leaves it out of the listing.
        var service = NewService();
        _api.Failing.Add(UrnB);
        await service.GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);
        Listing().Should().NotContain("feeds/bravo.xml");

        // Bravo recovers with nothing new, so no feed changes, but the listing must pick it up again.
        _api.Failing.Clear();
        await service.GenerateFeedsAsync(_podcasts, _config);

        Listing().Should().Contain("feeds/bravo.xml");
    }

    [Fact]
    public async Task Regeneration_WithIdenticalContent_LeavesTheFileUntouched()
    {
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);
        // Static-file ETags derive from mtime and length, so even a byte-identical rewrite would make
        // every podcatcher download the feed again. Backdate the file so a rewrite would show.
        var backdated = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FeedPath("alpha"), backdated);

        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.GetLastWriteTimeUtc(FeedPath("alpha")).Should().Be(backdated);
    }

    [Fact]
    public async Task Regeneration_WithANewEpisode_RewritesTheFile()
    {
        var service = NewService();
        await service.GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 3), FakeDrApi.Episodes(3));

        await service.GenerateFeedsAsync(_podcasts, _config);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-3");
    }

    [Fact]
    public async Task IncompleteEpisodeList_DoesNotReplaceAFullerFeed()
    {
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 10), FakeDrApi.Episodes(10));
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        // Episodes 7-10 now go missing at every page size, so the list stays short of totalSize.
        _api.DropFromPage = (_, e) => e.Order > 6;
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-10");
    }

    [Fact]
    public async Task IncompleteEpisodeList_StillReplacesASparserFeed()
    {
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 3), FakeDrApi.Episodes(3));
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 10), FakeDrApi.Episodes(10));
        _api.DropFromPage = (_, e) => e.Order > 6;
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-6");
    }

    [Fact]
    public async Task IncompleteEpisodeList_WithAsManyEpisodes_IsWrittenSoTheNewEpisodeArrives()
    {
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 6), FakeDrApi.Episodes(6));
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        // Episode 7 is published while episode 1 goes missing at every page size: still six episodes.
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 7), FakeDrApi.Episodes(7));
        _api.DropFromPage = (_, e) => e.Order == 1;
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-7");
    }

    [Fact]
    public async Task StreamOnlyEpisodes_AreLeftOutOfTheFeedAndTheListedCount()
    {
        var streamOnly = FakeDrApi.EpisodeNumber(3) with { AudioAssets = [new AudioAsset("HLS", 0, "https://api.dr.dk/stream.m3u8", null)] };
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 3), [FakeDrApi.EpisodeNumber(1), FakeDrApi.EpisodeNumber(2), streamOnly]);
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-2").And.NotContain("ep-3");
        // The listing names the newest episode the feed actually has, not the stream-only one.
        Listing().Should().Contain("2 afsnit").And.Contain(">Episode 2<").And.NotContain("Episode 3");

        // After a restart the feed is skipped as unchanged, and the count comes from the file.
        await NewService().GenerateFeedsAsync(_podcasts, _config);
        Listing().Should().Contain("2 afsnit");
    }

    [Fact]
    public async Task EpisodesThatAllLackAudio_DoNotEmptyAnExistingFeed()
    {
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        // A complete list, but DR's audio is missing from every episode for the moment.
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 2),
            [.. FakeDrApi.Episodes(2).Select(e => e with { AudioAssets = [new AudioAsset("HLS", 0, "https://api.dr.dk/s.m3u8", null)] })]);
        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-2");
    }

    [Fact]
    public async Task FailedLatestEpisodeLookup_DoesNotFailTheRefresh()
    {
        _api.FailRequest = u => u.Query.Contains("sort=desc");

        await NewService().GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        File.ReadAllText(FeedPath("alpha")).Should().Contain("ep-2");
    }

    [Fact]
    public async Task AscendingSeries_RotatedAudioHashOnTheNewestEpisode_IsPickedUp()
    {
        // DR re-encodes published episodes without bumping latestEpisodeStartTime. For an "Asc" series
        // the hash check used to inspect episode 1, so a rotation on the newest one went unnoticed.
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 2, defaultOrder: "Asc"), FakeDrApi.Episodes(2));
        var service = NewService();
        await service.GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        var rotated = new string('b', 64);
        _api.Set(UrnA, FakeDrApi.SeriesWith("Alpha", latestEpisode: 2, defaultOrder: "Asc"),
            [FakeDrApi.EpisodeNumber(1), FakeDrApi.EpisodeNumber(2, assetHash: rotated)]);
        await service.GenerateFeedsAsync(_podcasts, _config);

        File.ReadAllText(FeedPath("alpha")).Should().Contain(rotated);
    }

    [Fact]
    public async Task HttpClientTimeout_IsLoggedAsAFailureRatherThanThePerPodcastBudget()
    {
        var logger = new ListLogger<FeedGenerationService>();
        _api.Delay = TimeSpan.FromSeconds(5);

        var run = () => NewService(logger, httpTimeout: TimeSpan.FromMilliseconds(100))
            .GenerateFeedsAsync(_podcasts, _config, forceRegenerate: true);

        await run.Should().ThrowAsync<FeedGenerationService.FeedGenerationFailedException>();
        logger.Entries.Should().NotContain(e => e.Message.Contains("Timed out"))
            .And.Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Failed to process"));
    }
}

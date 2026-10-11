using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrPodcast.Tests;

public class DrApiClientTests
{
    private const string Urn = "urn:dr:radio:series:aaaaaaaaaaaaaaaaaaaaaaaa";

    private static DrApiClient ClientFor(FakeDrApi api, ILogger<DrApiClient>? logger = null) =>
        new(new StubHttpClientFactory(api), logger ?? NullLogger<DrApiClient>.Instance);

    [Fact]
    public async Task FetchAllEpisodes_ShortPages_RefetchesInSmallPagesAndRecoversEveryEpisode()
    {
        // DR's short pages: items missing from big pages while the next link still advances by the
        // full page size.
        var episodes = FakeDrApi.Episodes(40);
        var api = new FakeDrApi { DropFromPage = (pageSize, e) => pageSize > 5 && e.Order % 3 == 0 };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 40), episodes);

        var (result, complete) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(40), CancellationToken.None);

        result.Select(e => e.Id).Should().BeEquivalentTo(episodes.Select(e => e.Id));
        complete.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAllEpisodes_RefetchesOnlyTheShortPages()
    {
        // Only the second 256-item page is thinned out, so only its window is refetched (52 x 5 items).
        var episodes = FakeDrApi.Episodes(600);
        var api = new FakeDrApi { DropFromPage = (pageSize, e) => pageSize > 5 && e.Order is >= 300 and < 310 };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 600), episodes);

        var (result, complete) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(600), CancellationToken.None);

        result.Should().HaveCount(600);
        complete.Should().BeTrue();
        api.Requests.Count(u => u.Query.Contains("limit=5&")).Should().Be(52);
    }

    [Fact]
    public async Task FetchAllEpisodes_FailedRefetch_KeepsWhatTheFirstPassGotAndReportsIncomplete()
    {
        var api = new FakeDrApi
        {
            DropFromPage = (pageSize, e) => pageSize > 5 && e.Order % 3 == 0,
            FailRequest = u => u.Query.Contains("limit=5&"),
        };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 40), FakeDrApi.Episodes(40));

        var (result, complete) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(40), CancellationToken.None);

        result.Should().HaveCount(27);
        complete.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAllEpisodes_HttpTimeoutDuringRefetch_KeepsWhatTheFirstPassGot()
    {
        // HttpClient.Timeout surfaces as a cancellation, but it isn't the caller giving up.
        var api = new FakeDrApi
        {
            DropFromPage = (pageSize, e) => pageSize > 5 && e.Order % 3 == 0,
            Delay = TimeSpan.FromSeconds(5),
            DelayWhen = u => u.Query.Contains("limit=5&"),
        };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 40), FakeDrApi.Episodes(40));
        var client = new DrApiClient(new StubHttpClientFactory(api, TimeSpan.FromMilliseconds(100)), NullLogger<DrApiClient>.Instance);

        var (result, complete) = await client.FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(40), CancellationToken.None);

        result.Should().HaveCount(27);
        complete.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAllEpisodes_PaginationStoppingBeforeTheTotal_FetchesTheUnvisitedWindows()
    {
        var api = new FakeDrApi { NextStopsAt = 512 };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 600), FakeDrApi.Episodes(600));

        var (result, complete) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(600), CancellationToken.None);

        result.Should().HaveCount(600);
        complete.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAllEpisodes_EpisodesWithoutAnId_AreNotDuplicatedByTheRefetch()
    {
        var episodes = FakeDrApi.Episodes(10).Select(e => e with { Id = null }).ToList();
        var api = new FakeDrApi { DropFromPage = (pageSize, e) => pageSize > 5 && e.Order % 3 == 0 };
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 10), episodes);

        var (result, complete) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(10), CancellationToken.None);

        result.Select(e => e.Title).Should().OnlyHaveUniqueItems().And.HaveCount(10);
        complete.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAllEpisodes_CompleteFirstPass_IsNotRefetched()
    {
        var api = new FakeDrApi();
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 300), FakeDrApi.Episodes(300));

        var (result, _) = await ClientFor(api).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(300), CancellationToken.None);

        result.Should().HaveCount(300);
        api.Requests.Should().HaveCount(2, "300 episodes fit in two 256-item pages");
    }

    [Fact]
    public async Task FetchAllEpisodes_NothingAsNewAsTheSeriesLatest_WarnsThatTheListLooksIncomplete()
    {
        // The other signature of the short-page bug: the series' latest episode is from day 9, but the
        // newest one that came back is from day 2.
        var logger = new ListLogger<DrApiClient>();
        var api = new FakeDrApi();
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 9), FakeDrApi.Episodes(2));

        await ClientFor(api, logger).FetchAllEpisodesAsync(Urn, FakeDrApi.StartTimeOf(9), CancellationToken.None);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("looks incomplete"));
    }

    [Fact]
    public async Task FetchLatestEpisode_AscendingSeries_ReturnsTheNewestEpisode()
    {
        // In the endpoint's default order an "Asc" series lists its first episode first.
        var api = new FakeDrApi();
        api.Set(Urn, FakeDrApi.SeriesWith("S", latestEpisode: 3, defaultOrder: "Asc"), FakeDrApi.Episodes(3));

        var latest = await ClientFor(api).FetchLatestEpisodeAsync(Urn, FakeDrApi.StartTimeOf(3), CancellationToken.None);

        latest!.Id.Should().Be("ep-3");
    }

    [Fact]
    public void RegisteredHttpClient_DoesNotFollowRedirects()
    {
        // The API key is a custom header, which .NET keeps on a redirect to any host.
        var services = new ServiceCollection().AddLogging();
        DrApiClient.Register(services, apiKey: "test-key");
        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(DrApiClient.HttpClientName);
        while (handler is DelegatingHandler { InnerHandler: { } inner })
            handler = inner;

        handler.Should().BeOfType<SocketsHttpHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://api.dr.dk/radio/v2/series/urn:dr:radio:series:abc/episodes?limit=256&offset=256")]
    [InlineData("https://API.DR.DK/radio/v2/series/x/episodes?offset=512")]
    public void ResolveNextUrl_DrApiHttps_IsFollowed(string next)
    {
        DrApiClient.ResolveNextUrl(next).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/radio/v2/series/x/episodes?offset=256")]
    [InlineData("http://api.dr.dk/radio/v2/series/x/episodes")]
    [InlineData("https://evil.example/radio/v2/series/x/episodes")]
    [InlineData("https://api.dr.dk.evil.example/radio/v2/series/x/episodes")]
    [InlineData("https://user:pass@api.dr.dk/radio/v2/series/x/episodes")]
    [InlineData("https://api.dr.dk:8443/radio/v2/series/x/episodes")]
    [InlineData("file:///etc/passwd")]
    public void ResolveNextUrl_UntrustedOrMissing_ReturnsNull(string? next)
    {
        DrApiClient.ResolveNextUrl(next).Should().BeNull();
    }
}

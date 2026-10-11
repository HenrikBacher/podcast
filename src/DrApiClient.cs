namespace DrPodcast;

public sealed class DrApiClient(IHttpClientFactory httpClientFactory, ILogger<DrApiClient> logger)
{
    /// <summary>Name of the <see cref="HttpClient"/> registration made by <see cref="Register"/>.</summary>
    public const string HttpClientName = "DrApi";

    private const string ApiHost = "api.dr.dk";
    internal const string ApiUrl = $"https://{ApiHost}/radio/v2/series/";
    private const int EpisodesPerPage = 256;
    private const int MaxPagesPerSeries = 100;

    // DR's episodes endpoint can return short pages while its next links still advance the offset by
    // the full page size, silently skipping episodes. One series came back with 51 of its 128 episodes
    // in 256-item pages and 116 in 10-item pages; only 5-item pages were complete. Short pages are
    // refetched at this size.
    private const int FallbackEpisodesPerPage = 5;

    /// <summary>Registers the named HttpClient this class uses, and the class itself as a singleton.</summary>
    public static void Register(IServiceCollection services, string apiKey)
    {
        // Named (not typed) client on purpose: DrApiClient is resolved into the singleton
        // FeedGenerationService, and a typed client captured in a singleton never rotates its
        // handler. Resolving per call through the factory keeps handler lifetimes working.
        services.AddHttpClient(HttpClientName, client =>
            {
                client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
                client.Timeout = TimeSpan.FromSeconds(30);
                // GetAsync buffers the whole body (after decompression) before returning, so this caps
                // what a single response can cost in memory — a decompression bomb or runaway upstream
                // fails that podcast's refresh instead of exhausting the container. A full 256-episode
                // page is a few MB; the default limit is 2 GB.
                client.MaxResponseContentBufferSize = 32 * 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // HttpClient negotiates no compression by default, and episode pages are large JSON
                // (256 items each) that gzip to a fraction of their size.
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                // The API key is a custom header, which .NET (unlike Authorization) keeps on a redirect
                // to any host — the leak ResolveNextUrl guards against for pagination links.
                AllowAutoRedirect = false,
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<DrApiClient>();
    }

    public async Task<Series?> FetchSeriesAsync(string urn, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync($"{ApiUrl}{urn}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(
            stream,
            PodcastJsonContext.Default.Series,
            cancellationToken);
    }

    /// <summary>
    /// The series' newest episode. Warns when it doesn't start at <paramref name="latestEpisodeStartTime"/>
    /// (the series' own record), because the caller's audio-hash check would then inspect the wrong episode.
    /// </summary>
    public async Task<Episode?> FetchLatestEpisodeAsync(string urn, string? latestEpisodeStartTime, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        // Without sort=desc the endpoint follows the series' defaultOrder, so for an "Asc" series
        // limit=1 is its first episode. The parameter is undocumented, hence the check below.
        var page = await FetchPageAsync(client, $"{ApiUrl}{urn}/episodes?limit=1&sort=desc", cancellationToken);
        var latest = page?.Items is { Count: > 0 } items ? items[0] : null;

        if (ParseTime(latestEpisodeStartTime) is { } expected && ParseTime(latest?.StartTime) is { } got && got != expected)
        {
            logger.LogWarning("Latest-episode lookup for {Urn} returned an episode starting {Got:u}, but the series' latest starts {Expected:u}. The audio-hash check may be inspecting the wrong episode.",
                urn, got, expected);
        }
        return latest;
    }

    /// <summary>
    /// Every episode of the series. Pages that come back short are refetched in small pages and merged.
    /// <c>Complete</c> is false when the result still falls short of the total the API reports; a warning
    /// is also logged when nothing is as new as <paramref name="latestEpisodeStartTime"/>.
    /// </summary>
    public async Task<(List<Episode> Episodes, bool Complete)> FetchAllEpisodesAsync(string urn, string? latestEpisodeStartTime, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        List<Episode> episodes = new(EpisodesPerPage);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<(int Offset, int Length)> shortPages = [];
        int? totalSize = null;

        var initialUrl = $"{ApiUrl}{urn}/episodes?limit={EpisodesPerPage}";
        string? nextUrl = initialUrl;
        var nextOffset = 0;
        for (var pageCount = 1; !string.IsNullOrEmpty(nextUrl); pageCount++)
        {
            if (pageCount > MaxPagesPerSeries)
            {
                logger.LogWarning("Reached page limit ({MaxPages}) fetching episodes from {Url}. Truncating.", MaxPagesPerSeries, initialUrl);
                break;
            }
            var page = await FetchPageAsync(client, nextUrl, cancellationToken);
            totalSize ??= page?.TotalSize;
            var items = page?.Items ?? [];
            Add(items);

            nextUrl = ResolveNextUrl(page?.Next);
            if (nextUrl is null && !string.IsNullOrEmpty(page?.Next))
                logger.LogWarning("Ignoring untrusted pagination link {Next} while fetching {Url}. Truncating.", page.Next, initialUrl);

            // A page holds the full page size unless it's the last one. Comparing what came back with
            // what should have finds the windows DR silently thinned out.
            var offset = page?.Offset ?? (pageCount - 1) * EpisodesPerPage;
            var expected = totalSize is { } total ? Math.Min(EpisodesPerPage, total - offset) : nextUrl is null ? items.Count : EpisodesPerPage;
            if (items.Count < expected)
                shortPages.Add((offset, expected));
            nextOffset = offset + EpisodesPerPage;
        }

        // Pagination that stopped before the reported total (no next link, an untrusted one, or the
        // page limit) leaves windows that were never visited; they're refetched the same way.
        if (totalSize is { } reported)
        {
            for (var o = nextOffset; o < Math.Min(reported, MaxPagesPerSeries * EpisodesPerPage); o += EpisodesPerPage)
                shortPages.Add((o, Math.Min(EpisodesPerPage, reported - o)));
        }

        if (shortPages.Count > 0)
        {
            var before = episodes.Count;
            try
            {
                // Offsets are built here rather than taken from next links, so only the thinned-out
                // windows are refetched (at most 52 requests each).
                foreach (var (offset, length) in shortPages)
                {
                    for (var o = offset; o < offset + length; o += FallbackEpisodesPerPage)
                        Add((await FetchPageAsync(client, $"{ApiUrl}{urn}/episodes?limit={FallbackEpisodesPerPage}&offset={o}", cancellationToken))?.Items ?? []);
                }
                logger.LogInformation("DR API returned {ShortPages} short or missing page(s) for {Urn}; refetching them in {FallbackPageSize}-item pages added {Added} episodes ({Count}/{Total}).",
                    shortPages.Count, urn, FallbackEpisodesPerPage, episodes.Count - before, episodes.Count, totalSize);
            }
            // Anything but the caller giving up, including an HttpClient timeout (which also surfaces as
            // a cancellation): keep what was fetched so far.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Refetching short pages for {Urn} failed; keeping the {Count} episodes fetched so far.", urn, episodes.Count);
            }
        }

        var complete = !(episodes.Count < totalSize);
        var newest = episodes.Max(e => ParseTime(e.StartTime));
        if (!complete || newest < ParseTime(latestEpisodeStartTime))
        {
            logger.LogWarning("Episode list for {Urn} looks incomplete: {Count} of {Total} episodes, the newest starting {Newest:u}, but the series' latest starts {Latest}.",
                urn, episodes.Count, totalSize, newest, latestEpisodeStartTime);
        }

        return (episodes, complete);

        // The same episode can come back twice: from a refetched window, or on two offset pages when
        // one is published mid-fetch. Episodes without an id are matched on title and start time.
        void Add(List<Episode> items)
        {
            foreach (var episode in items)
            {
                if (seen.Add(episode.Id ?? $"{episode.Title}\n{episode.StartTime}"))
                    episodes.Add(episode);
            }
        }
    }

    private static async Task<EpisodesPage?> FetchPageAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);
    }

    private static DateTimeOffset? ParseTime(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, RssBuilder.UtcParseStyles, out var time) ? time : null;

    /// <summary>
    /// Returns the pagination link only if it points back at the DR API over HTTPS. The client
    /// sends the API key on every request, so following an arbitrary <c>next</c> from the response
    /// body would hand the key (and a request) to whatever host the upstream named.
    /// </summary>
    internal static string? ResolveNextUrl(string? next) =>
        Uri.TryCreate(next, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri
            : null;
}

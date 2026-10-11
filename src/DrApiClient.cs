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
    // in 256-item pages and 116 in 10-item pages; only 5-item pages were complete.
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
        using var response = await client.GetAsync($"{ApiUrl}{urn}/episodes?limit=1&sort=desc", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);
        var latest = page?.Items is { Count: > 0 } items ? items[0] : null;

        if (ParseTime(latestEpisodeStartTime) is { } expected && ParseTime(latest?.StartTime) is { } got && got != expected)
        {
            logger.LogWarning("Latest-episode lookup for {Urn} returned an episode starting {Got:u}, but the series' latest starts {Expected:u}. The audio-hash check may be inspecting the wrong episode.",
                urn, got, expected);
        }
        return latest;
    }

    /// <summary>
    /// Every episode of the series. A fetch that comes up short of the API's reported total is repeated
    /// in small pages and merged. Warns when the result still looks incomplete: fewer episodes than the
    /// total, or none as new as <paramref name="latestEpisodeStartTime"/>.
    /// </summary>
    public async Task<List<Episode>> FetchAllEpisodesAsync(string urn, string? latestEpisodeStartTime, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        List<Episode> episodes = new(EpisodesPerPage);
        HashSet<string> seenIds = new(StringComparer.Ordinal);

        var totalSize = await CollectAsync(EpisodesPerPage, MaxPagesPerSeries);
        if (totalSize is { } total && episodes.Count < total)
        {
            var firstPassCount = episodes.Count;
            // One page beyond what the reported total needs, so a next link that never ends can't run away.
            var maxPages = (Math.Min(total, MaxPagesPerSeries * EpisodesPerPage) + FallbackEpisodesPerPage - 1) / FallbackEpisodesPerPage + 1;
            await CollectAsync(FallbackEpisodesPerPage, maxPages);
            if (episodes.Count >= total)
            {
                logger.LogInformation("DR API returned {FirstPass}/{Total} episodes for {Urn} in {PageSize}-item pages; recovered all of them in {FallbackPageSize}-item pages.",
                    firstPassCount, total, urn, EpisodesPerPage, FallbackEpisodesPerPage);
            }
        }

        var newest = episodes.Max(e => ParseTime(e.StartTime));
        if (episodes.Count < totalSize || newest < ParseTime(latestEpisodeStartTime))
        {
            logger.LogWarning("Episode list for {Urn} looks incomplete: {Count} of {Total} episodes, the newest starting {Newest:u}, but the series' latest starts {Latest}. The feed will be missing episodes.",
                urn, episodes.Count, totalSize, newest, latestEpisodeStartTime);
        }

        return episodes;

        // Follows the pagination at pageSize, adding each episode not seen yet (the same one can come back
        // in both passes, or on two offset pages when one is published mid-fetch). Returns the API's total.
        async Task<int?> CollectAsync(int pageSize, int maxPages)
        {
            var initialUrl = $"{ApiUrl}{urn}/episodes?limit={pageSize}";
            string? nextUrl = initialUrl;
            int? total = null;
            var pageCount = 0;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                if (++pageCount > maxPages)
                {
                    logger.LogWarning("Reached page limit ({MaxPages}) fetching episodes from {Url}. Truncating.", maxPages, initialUrl);
                    break;
                }
                using var response = await client.GetAsync(nextUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var page = await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);

                total ??= page?.TotalSize;
                foreach (var episode in page?.Items ?? [])
                {
                    if (episode.Id is not { } id || seenIds.Add(id))
                        episodes.Add(episode);
                }

                nextUrl = ResolveNextUrl(page?.Next);
                if (nextUrl is null && !string.IsNullOrEmpty(page?.Next))
                    logger.LogWarning("Ignoring untrusted pagination link {Next} while fetching {Url}. Truncating.", page.Next, initialUrl);
            }

            return total;
        }
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

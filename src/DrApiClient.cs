namespace DrPodcast;

public sealed class DrApiClient(IHttpClientFactory httpClientFactory, ILogger<DrApiClient> logger)
{
    /// <summary>Name of the configured <see cref="HttpClient"/> registration in Program.cs.</summary>
    public const string HttpClientName = "DrApi";

    private const string ApiHost = "api.dr.dk";
    private const string ApiUrl = $"https://{ApiHost}/radio/v2/series/";
    private const int EpisodesPerPage = 256;
    private const int MaxPagesPerSeries = 100;

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

    public async Task<Episode?> FetchLatestEpisodeAsync(string urn, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync($"{ApiUrl}{urn}/episodes?limit=1", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);
        return page?.Items is { Count: > 0 } items ? items[0] : null;
    }

    public async Task<List<Episode>?> FetchAllEpisodesAsync(string urn, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var initialUrl = $"{ApiUrl}{urn}/episodes?limit={EpisodesPerPage}";
        List<Episode> allEpisodes = new(EpisodesPerPage);

        string? nextUrl = initialUrl;
        var pageCount = 0;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            if (++pageCount > MaxPagesPerSeries)
            {
                logger.LogWarning("Reached page limit ({MaxPages}) fetching episodes from {Url}. Truncating.", MaxPagesPerSeries, initialUrl);
                break;
            }
            using var response = await client.GetAsync(nextUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);

            if (page?.Items is { } episodes)
                allEpisodes.AddRange(episodes);

            nextUrl = ResolveNextUrl(page?.Next);
            if (nextUrl is null && !string.IsNullOrEmpty(page?.Next))
                logger.LogWarning("Ignoring untrusted pagination link {Next} while fetching {Url}. Truncating.", page.Next, initialUrl);
        }

        return allEpisodes;
    }

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

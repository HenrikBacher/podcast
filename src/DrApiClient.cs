namespace DrPodcast;

public sealed class DrApiClient(IHttpClientFactory httpClientFactory, ILogger<DrApiClient> logger)
{
    private const string ApiUrl = "https://api.dr.dk/radio/v2/series/";
    private const int EpisodesPerPage = 256;
    private const int MaxPagesPerSeries = 100;

    public async Task<Series?> FetchSeriesAsync(string urn, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("DrApi");
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
        var client = httpClientFactory.CreateClient("DrApi");
        using var response = await client.GetAsync($"{ApiUrl}{urn}/episodes?limit=1", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync(stream, PodcastJsonContext.Default.EpisodesPage, cancellationToken);
        return page?.Items is { Count: > 0 } items ? items[0] : null;
    }

    public async Task<List<Episode>?> FetchAllEpisodesAsync(string urn, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("DrApi");
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

            nextUrl = page?.Next;
        }

        return allEpisodes;
    }
}

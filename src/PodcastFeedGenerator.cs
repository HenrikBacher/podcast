using DrPodcast;

const string ApiUrl = "https://api.dr.dk/radio/v2/series/";
const string Rfc822Format = "ddd, dd MMM yyyy HH:mm:ss zzz";

string apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";

ServiceCollection services = [];
services.AddLogging(builder =>
{
    builder.AddConsole(options => options.FormatterName = "simple");
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddHttpClient("DrApi", client =>
{
    client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler((provider, _) =>
{
    var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("HttpRetry");
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => !msg.IsSuccessStatusCode)
        .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, _) =>
                logger.LogWarning("Retry {RetryCount} after {Delay}s - {Reason}",
                    retryCount, timespan.TotalSeconds,
                    outcome.Exception?.Message ?? $"Status {outcome.Result?.StatusCode}"));
});

await using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PodcastFeedGenerator");

var podcastList = JsonSerializer.Deserialize(
    await File.ReadAllTextAsync("podcasts.json"),
    PodcastJsonContext.Default.PodcastList);

var config = new GeneratorConfig();

// Create feeds directory once before processing (instead of for each podcast)
Directory.CreateDirectory(config.FeedsDir);

// Generate RSS feeds and collect metadata
var tasks = podcastList?.Podcasts.Select(podcast =>
    ProcessPodcastAsync(podcast, httpClientFactory, baseUrl, config, logger)).ToArray();

var results = await Task.WhenAll(tasks!);
var feedMetadata = results.OfType<FeedMetadata>().ToList();

logger.LogInformation("Generated {FeedCount} podcast feeds", feedMetadata.Count);

// Generate website using collected metadata
await WebsiteGenerator.GenerateAsync(feedMetadata, config, logger);

static async Task<FeedMetadata?> ProcessPodcastAsync(Podcast podcast, IHttpClientFactory factory, string baseUrl, GeneratorConfig config, ILogger logger)
{
    try
    {
        using var httpClient = factory.CreateClient("DrApi");

        var seriesResponse = await httpClient.GetAsync($"{ApiUrl}{podcast.Urn}");
        seriesResponse.EnsureSuccessStatusCode();

        // Use streaming to avoid allocating entire response as string
        await using var stream = await seriesResponse.Content.ReadAsStreamAsync();
        var series = await JsonSerializer.DeserializeAsync(
            stream,
            PodcastJsonContext.Default.Series);

        var episodes = await FetchAllEpisodesAsync($"{ApiUrl}{podcast.Urn}/episodes?limit=256", httpClient);

        var (rss, metadata) = BuildRssFeed(series, episodes, podcast, baseUrl);

        // Save using async I/O for better performance
        string outputPath = Path.Combine(config.FeedsDir, $"{podcast.Slug}.xml");
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using var writer = new StreamWriter(fileStream, new UTF8Encoding(false));
        var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);
        await writer.WriteAsync(xmlDoc.ToString());
        logger.LogInformation("Generated {Slug}", podcast.Slug);

        return metadata;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process {Urn}", podcast.Urn);
        return null;
    }
}

static (XElement rss, FeedMetadata metadata) BuildRssFeed(Series? series, List<Episode>? episodes, Podcast podcast, string baseUrl)
{
    XNamespace atom = "http://www.w3.org/2005/Atom";
    XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    XNamespace media = "http://search.yahoo.com/mrss/";

    var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets)
                   ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets);

    var lastBuildDate = DateTime.TryParse(series?.LatestEpisodeStartTime, out var dt)
        ? dt.ToString(Rfc822Format, CultureInfo.InvariantCulture)
        : DateTime.Now.ToString(Rfc822Format, CultureInfo.InvariantCulture);

    var itunesType = DetermineItunesType(series);

    var title = series?.Title ?? podcast.Slug.Replace("-", " ");
    var cleanTitle = RegexCache.FeedTitleCleanup.Replace(title, "").Trim();

    var channel = new XElement("channel",
        new XElement(atom + "link",
            new XAttribute("href", $"{baseUrl}/feeds/{podcast.Slug}.xml"),
            new XAttribute("rel", "self"),
            new XAttribute("type", "application/rss+xml")),
        new XElement("title", series?.Title),
        new XElement("link", series?.PresentationUrl),
        new XElement("description", series?.Description),
        new XElement("language", "da"),
        new XElement("copyright", "DR"),
        new XElement("lastBuildDate", lastBuildDate),
        new XElement(itunes + "explicit", series?.ExplicitContent),
        new XElement(itunes + "author", "DR"),
        new XElement(itunes + "block", "yes"),
        new XElement(itunes + "owner",
            new XElement(itunes + "email", "podcast@dr.dk"),
            new XElement(itunes + "name", "DR")),
        new XElement(itunes + "type", itunesType));

    if (!string.IsNullOrEmpty(imageUrl))
    {
        channel.Add(new XElement(itunes + "image", new XAttribute("href", imageUrl)));
    }

    if (!string.IsNullOrEmpty(series?.Punchline))
    {
        channel.Add(new XElement(itunes + "subtitle", series.Punchline));
    }

    if (!string.IsNullOrEmpty(series?.Description))
    {
        channel.Add(new XElement(itunes + "summary", series.Description));
    }

    AddCategories(channel, series?.Categories, itunes);

    if (series?.NumberOfSeries > 0)
    {
        channel.Add(new XElement(itunes + "season", series.NumberOfSeries));
    }

    if (episodes is not null)
    {
        IOrderedEnumerable<Episode> sorted;

        if (series?.NumberOfSeries > 0)
        {
            // For shows with seasons, sort by season descending (latest first), then by order
            sorted = series?.DefaultOrder == "Asc"
                ? episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenBy(e => e.Order ?? long.MaxValue)
                : episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenByDescending(e => e.Order ?? long.MinValue);
        }
        else
        {
            // For non-seasonal shows, use standard order
            sorted = series?.DefaultOrder == "Asc"
                ? episodes.OrderBy(e => e.Order ?? long.MaxValue)
                : episodes.OrderByDescending(e => e.Order ?? long.MinValue);
        }

        foreach (var episode in sorted)
        {
            channel.Add(BuildEpisodeItem(episode, imageUrl, itunes, media));
        }
    }

    var rss = new XElement("rss",
        new XAttribute("version", "2.0"),
        new XAttribute(XNamespace.Xmlns + "atom", atom),
        new XAttribute(XNamespace.Xmlns + "itunes", itunes),
        new XAttribute(XNamespace.Xmlns + "media", media),
        channel);

    var metadata = new FeedMetadata(podcast.Slug, cleanTitle, imageUrl);

    return (rss, metadata);
}

static string DetermineItunesType(Series? series) =>
      series?.PresentationType == "Show" ? "serial" : "episodic";

static void AddCategories(XElement element, List<string>? categories, XNamespace itunes)
{
    if (categories is null) return;

    // Avoid LINQ overhead - use direct foreach
    foreach (var category in categories)
    {
        if (string.IsNullOrEmpty(category)) continue;
        element.Add(new XElement(itunes + "category", new XAttribute("text", category)));
    }
}

static XElement BuildEpisodeItem(Episode episode, string? channelImage, XNamespace itunes, XNamespace media)
{
    var audioAsset = episode.AudioAssets?
        .Where(a => a?.Format == "mp3")
        .OrderByDescending(a => a?.Bitrate ?? 0)
        .FirstOrDefault();

    var imageUrl = PodcastHelpers.GetImageUrlFromAssets(episode.ImageAssets) ?? channelImage;
    var duration = episode.DurationMilliseconds is not null
        ? TimeSpan.FromMilliseconds(episode.DurationMilliseconds.Value).ToString(@"hh\:mm\:ss")
        : "";

    var pubDate = DateTime.TryParse(episode.PublishTime, out var dt)
        ? dt.ToString(Rfc822Format, CultureInfo.InvariantCulture)
        : episode.PublishTime ?? "";

    var item = new XElement("item",
        new XElement("title", episode.Title ?? ""),
        new XElement("description", episode.Description ?? ""),
        new XElement("pubDate", pubDate),
        new XElement(itunes + "explicit", episode.ExplicitContent),
        new XElement(itunes + "author", "DR"),
        new XElement(itunes + "duration", duration)
        );

    if (!string.IsNullOrEmpty(imageUrl))
    {
        item.Add(new XElement(itunes + "image", new XAttribute("href", imageUrl)));
    }

    if (episode.EpisodeNumber is not null)
    {
        item.Add(new XElement(itunes + "episode", episode.EpisodeNumber.Value));
    }

    if (episode.SeasonNumber is not null)
    {
        item.Add(new XElement(itunes + "season", episode.SeasonNumber.Value));
    }

    if (!string.IsNullOrEmpty(episode.PresentationUrl))
    {
        item.Add(new XElement("link", episode.PresentationUrl));
    }

    if (!string.IsNullOrEmpty(episode.Id))
    {
        item.Add(new XElement("guid", new XAttribute("isPermalink", "false"), episode.Id));
    }

    if (audioAsset?.Url is { } url && !string.IsNullOrEmpty(url))
    {
        var mimeType = GetMimeTypeFromFormat(audioAsset.Format);
        var enclosure = new XElement("enclosure",
            new XAttribute("url", url),
            new XAttribute("type", mimeType));

        if (audioAsset.FileSize is not null)
        {
            enclosure.Add(new XAttribute("length", audioAsset.FileSize.Value));
        }

        item.Add(enclosure);
    }

    AddCategories(item, episode.Categories, itunes);

    return item;
}

static string GetMimeTypeFromFormat(string? format)
{
    if (format is null) return "audio/mpeg";

    // Use ordinal comparison to avoid allocation from ToLowerInvariant()
    return format.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" :
           format.Equals("aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" :
           format.Equals("m4a", StringComparison.OrdinalIgnoreCase) ? "audio/mp4" :
           format.Equals("ogg", StringComparison.OrdinalIgnoreCase) ? "audio/ogg" :
           format.Equals("wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" :
           format.Equals("flac", StringComparison.OrdinalIgnoreCase) ? "audio/flac" :
           "audio/mpeg"; // Default fallback
}

static async Task<List<Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient)
{
    // Parse limit from URL to preallocate capacity (reduces array reallocations)
    var limitMatch = Regex.Match(initialUrl, @"limit=(\d+)");
    var estimatedCapacity = limitMatch.Success ? int.Parse(limitMatch.Groups[1].Value) : 256;
    List<Episode> allEpisodes = new(estimatedCapacity);

    string? nextUrl = initialUrl;

    while (!string.IsNullOrEmpty(nextUrl))
    {
        var response = await httpClient.GetAsync(nextUrl);
        response.EnsureSuccessStatusCode();

        // Use streaming to avoid allocating entire response as string
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            // Deserialize directly from JsonElement instead of GetRawText() to avoid double parsing
            var episodes = items.Deserialize(PodcastJsonContext.Default.ListEpisode);
            if (episodes != null)
            {
                allEpisodes.AddRange(episodes);
            }
        }

        nextUrl = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
            ? next.GetString()
            : null;
    }

    return allEpisodes;
}

// Compiled regex cache for better performance
file static class RegexCache
{
    public static readonly Regex FeedTitleCleanup = new(@"\s*\([^)]*feed[^)]*\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}

using DrPodcast;

const string ApiUrl = "https://api.dr.dk/radio/v2/series/";
const string Rfc822Format = "ddd, dd MMM yyyy HH:mm:ss zzz";

string apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";

ServiceCollection services = [];
services.AddHttpClient("DrApi", client =>
{
    client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => !msg.IsSuccessStatusCode)
    .WaitAndRetryAsync(3,
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (outcome, timespan, retryCount, _) =>
            Console.WriteLine($"Retry {retryCount} after {timespan} seconds")));

await using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

var podcastList = JsonSerializer.Deserialize(
    await File.ReadAllTextAsync("podcasts.json"),
    PodcastJsonContext.Default.PodcastList);

var config = new GeneratorConfig();

// Generate RSS feeds and collect metadata
var tasks = podcastList?.Podcasts.Select(podcast =>
    ProcessPodcastAsync(podcast, httpClientFactory, baseUrl, config)).ToArray();

var results = await Task.WhenAll(tasks!);
var feedMetadata = results.Where(m => m != null).Cast<FeedMetadata>().ToList();

Console.WriteLine($"\nGenerated {feedMetadata.Count} podcast feeds.");

// Generate website using collected metadata
WebsiteGenerator.Generate(feedMetadata, config);

static async Task<FeedMetadata?> ProcessPodcastAsync(Podcast podcast, IHttpClientFactory factory, string baseUrl, GeneratorConfig config)
{
    try
    {
        using var httpClient = factory.CreateClient("DrApi");

        var seriesResponse = await httpClient.GetAsync($"{ApiUrl}{podcast.Urn}");
        seriesResponse.EnsureSuccessStatusCode();

        var series = JsonSerializer.Deserialize(
            await seriesResponse.Content.ReadAsStringAsync(),
            PodcastJsonContext.Default.Series);

        var episodes = await FetchAllEpisodesAsync($"{ApiUrl}{podcast.Urn}/episodes?limit=256", httpClient);

        var (rss, metadata) = BuildRssFeed(series, episodes, podcast, baseUrl);

        // Ensure feeds directory exists
        Directory.CreateDirectory(config.FeedsDir);

        // Save directly to final location
        string outputPath = Path.Combine(config.FeedsDir, $"{podcast.Slug}.xml");
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss).Save(outputPath);
        Console.WriteLine($"✓ Generated {podcast.Slug}");

        return metadata;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Failed to process {podcast.Urn}: {ex.Message}");
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
    var cleanTitle = Regex.Replace(title, @"\s*\([^)]*feed[^)]*\)\s*$", "", RegexOptions.IgnoreCase).Trim();

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

static string DetermineItunesType(Series? series)
{
    if (series?.PresentationType == "Show")
    {
        return "serial";
    }
    else
    {
        return "episodic";
    }
}

static void AddCategories(XElement element, List<string>? categories, XNamespace itunes)
{
    if (categories is not null)
    {
        foreach (var category in categories.Where(c => !string.IsNullOrEmpty(c)))
    {
        element.Add(new XElement(itunes + "category", new XAttribute("text", category)));
    }
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
    return format?.ToLowerInvariant() switch
    {
        "mp3" => "audio/mpeg",
        "aac" => "audio/aac",
        "m4a" => "audio/mp4",
        "ogg" => "audio/ogg",
        "wav" => "audio/wav",
        "flac" => "audio/flac",
        _ => "audio/mpeg" // Default fallback
    };
}

static async Task<List<Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient)
{
    List<Episode> allEpisodes = [];
    string? nextUrl = initialUrl;

    while (!string.IsNullOrEmpty(nextUrl))
    {
        var response = await httpClient.GetAsync(nextUrl);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var episodes = JsonSerializer.Deserialize(items.GetRawText(), PodcastJsonContext.Default.ListEpisode);
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

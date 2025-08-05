using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using DrPodcast;

var services = new ServiceCollection();

// Get environment variables with defaults
string apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
const string apiUrl = "https://api.dr.dk/radio/v2/series/";

// Configure HttpClient with Polly retry policy
services.AddHttpClient("DrApi", client =>
{
    client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy());

await using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

// Deserialize podcasts.json using JsonSerializerContext for trimming compatibility
var podcastList = JsonSerializer.Deserialize(
    await File.ReadAllTextAsync("podcasts.json"), PodcastJsonContext.Default.PodcastList);

Directory.CreateDirectory("output");

// Performance: Use a single HttpClient instance for all requests
var httpClient = httpClientFactory.CreateClient("DrApi");

// Performance: Limit concurrency to avoid overwhelming API/system
int maxConcurrency = 5;
using var semaphore = new SemaphoreSlim(maxConcurrency);

// Performance: Cache for category mapping
var categoryCache = new Dictionary<string, string>();

// Helper for cached category mapping
string CachedMapToPodcastCategory(string category)
{
    if (categoryCache.TryGetValue(category, out var mapped))
        return mapped;
    mapped = MapToPodcastCategory(category);
    categoryCache[category] = mapped;
    return mapped;
}

// Performance: Increase episode fetch limit to 512 if API supports
int episodeFetchLimit = 512;

var tasks = podcastList?.Podcasts.Select(async podcast =>
{
    await semaphore.WaitAsync();
    try
    {
        string urn = podcast.Urn;
        string slug = podcast.Slug;
        string seriesUrl = apiUrl + urn;

        // Use shared HttpClient
        // First, fetch series information
        var seriesResponse = await httpClient.GetAsync(seriesUrl);
        seriesResponse.EnsureSuccessStatusCode();
        string seriesContent = await seriesResponse.Content.ReadAsStringAsync();
        var series = JsonSerializer.Deserialize(seriesContent, PodcastJsonContext.Default.Series);

        // Fetch all episodes, handling pagination
        var episodes = await FetchAllEpisodesAsync($"{apiUrl}{urn}/episodes?limit={episodeFetchLimit}", httpClient);

        // Build channel model using object initializer and with expressions
        var channelModel = new Channel
        {
            Title = series?.Title + " (Reproduceret feed)",
            Link = series?.PresentationUrl ?? $"https://www.dr.dk/lyd/special-radio/{slug}",
            Description = series?.Description ?? series?.Punchline ?? $"Feed for {slug}",
            Language = "da",
            Copyright = "DR",
            LastBuildDate = DateTime.TryParse(series?.LatestEpisodeStartTime, out var lastEpisode)
                ? lastEpisode.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)
                : DateTime.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture),
            Explicit = series?.ExplicitContent == true ? "yes" : "no",
            Author = "DR",
            Block = "yes",
            Owner = new ChannelOwner { Email = "podcast@dr.dk", Name = "DR" },
            NewFeedUrl = $"{baseUrl}/feeds/{slug}.xml",
            Image = GetImageUrlFromAssets(series?.ImageAssets) ?? GetImageUrlFromAssets(podcast.ImageAssets)
        };

        // XML namespaces for RSS/iTunes
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        XNamespace media = "http://search.yahoo.com/mrss/";

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", $"{baseUrl}/feeds/{slug}.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")
            ),
            new XElement("title", channelModel.Title),
            new XElement("link", channelModel.Link),
            new XElement("description", channelModel.Description),
            new XElement("language", channelModel.Language),
            new XElement("copyright", channelModel.Copyright),
            new XElement("lastBuildDate", channelModel.LastBuildDate),
            new XElement(itunes + "explicit", channelModel.Explicit),
            new XElement(itunes + "author", channelModel.Author),
            new XElement(itunes + "block", channelModel.Block),
            new XElement(itunes + "owner",
                new XElement(itunes + "email", channelModel.Owner?.Email),
                new XElement(itunes + "name", channelModel.Owner?.Name)
            ),
            new XElement(itunes + "new-feed-url", channelModel.NewFeedUrl),
            new XElement(itunes + "image", new XAttribute("href", channelModel.Image ?? ""))
        );

        // Add iTunes categories from series data using modern collection expressions
        if (series?.Categories is { Count: > 0 } categories)
        {
            foreach (var category in categories.Where(c => !string.IsNullOrEmpty(c)))
            {
                var mapped = CachedMapToPodcastCategory(category);
                if (!string.IsNullOrEmpty(mapped))
                    channel.Add(new XElement(itunes + "category", new XAttribute("text", mapped)));
            }
        }

        // Add series summary/subtitle if available
        if (!string.IsNullOrEmpty(series?.Punchline))
            channel.Add(new XElement(itunes + "subtitle", series.Punchline));

        if (!string.IsNullOrEmpty(series?.Description))
            channel.Add(new XElement(itunes + "summary", series.Description));

        // Determine iTunes type using pattern matching
        string itunesType = series?.DefaultOrder?.ToLowerInvariant() switch
        {
            "desc" => "episodic",
            "asc" => "serial",
            _ => series?.PresentationType?.ToLowerInvariant() switch
            {
                "ongoing" => "episodic",
                "show" => "serial",
                _ => series?.GroupingType?.ToLowerInvariant() switch
                {
                    "yearly" => "episodic",
                    "seasons" => "serial",
                    _ => "episodic"
                }
            }
        };

        channel.Add(new XElement(itunes + "type", itunesType));

        // Add season information for seasonal content
        if (series?.GroupingType?.Contains("Seasons", StringComparison.OrdinalIgnoreCase) == true)
        {
            int seasonCount = series.NumberOfSeasons > 0 ? series.NumberOfSeasons : series.NumberOfSeries;
            if (seasonCount > 0)
                channel.Add(new XElement(itunes + "season", seasonCount));
        }

        if (episodes is { Count: > 0 })
        {
            // Sort episodes based on the determined podcast type using modern LINQ
            var sortedEpisodes = itunesType == "serial"
                ? episodes.OrderBy(ep => DateTime.TryParse(ep.PublishTime, out var dt) ? dt : DateTime.MinValue)
                : episodes.OrderByDescending(ep => DateTime.TryParse(ep.PublishTime, out var dt) ? dt : DateTime.MinValue);

            foreach (var episode in sortedEpisodes)
            {
                string epTitle = episode.Title ?? "";
                string epDesc = episode.Description ?? "";
                string epPubDate = episode.PublishTime ?? "";
                string epGuid = episode.Id ?? Guid.NewGuid().ToString();
                string epLink = episode.PresentationUrl ?? "";
                string? epImage = GetImageUrlFromAssets(episode.ImageAssets) ?? channelModel.Image;

                // Select the highest quality mp3 using LINQ and pattern matching
                var (epAudio, epAudioLength) = episode.AudioAssets?
                    .Where(a => a?.Format == "mp3")
                    .OrderByDescending(a => a?.Bitrate ?? 0)
                    .FirstOrDefault() is { } best
                    ? (best.Url ?? "", best.FileSize ?? 0)
                    : ("", 0);

                // Format iTunes duration using modern TimeSpan formatting
                string itunesDuration = episode.DurationMilliseconds > 0
                    ? TimeSpan.FromMilliseconds(episode.DurationMilliseconds.Value).ToString(@"hh\:mm\:ss")
                    : "";

                var item = new XElement("item",
                    new XElement("title", epTitle),
                    new XElement("description", epDesc),
                    new XElement("pubDate", DateTime.TryParse(epPubDate, out var dt)
                        ? dt.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)
                        : epPubDate),
                    new XElement("explicit", episode.ExplicitContent ? "yes" : "no"),
                    new XElement(itunes + "author", "DR"),
                    new XElement(itunes + "image", new XAttribute("href", epImage ?? "")),
                    new XElement(itunes + "duration", itunesDuration),
                    new XElement(media + "restriction",
                        new XAttribute("relationship", "allow"),
                        new XAttribute("type", "country"),
                        "dk"
                    )
                );

                // Add iTunes episode metadata
                item.Add(new XElement(itunes + "episodeType", "full"));

                if (episode.EpisodeNumber is > 0)
                    item.Add(new XElement(itunes + "episode", episode.EpisodeNumber.Value));

                if (episode.SeasonNumber is > 0)
                    item.Add(new XElement(itunes + "season", episode.SeasonNumber.Value));

                if (!string.IsNullOrEmpty(epLink))
                    item.Add(new XElement("link", epLink));

                if (!string.IsNullOrEmpty(epGuid))
                    item.Add(new XElement("guid", new XAttribute("isPermalink", "false"), epGuid));

                if (!string.IsNullOrEmpty(epAudio))
                {
                    var enclosure = new XElement("enclosure",
                        new XAttribute("url", epAudio),
                        new XAttribute("type", "audio/mpeg"));

                    if (epAudioLength > 0)
                        enclosure.Add(new XAttribute("length", epAudioLength));

                    item.Add(enclosure);
                }

                // Add episode categories
                if (episode.Categories is { Count: > 0 } episodeCategories)
                {
                    foreach (var cat in episodeCategories)
                    {
                        var mapped = CachedMapToPodcastCategory(cat);
                        if (!string.IsNullOrEmpty(mapped))
                            item.Add(new XElement(itunes + "category", new XAttribute("text", mapped)));
                    }
                }

                channel.Add(item);
            }
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            new XAttribute(XNamespace.Xmlns + "media", media),
            channel
        );

        string outputPath = Path.Combine("output", $"{slug}.xml");
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);

        // Performance: Use async XML writing if available
        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        using var writer = new StreamWriter(stream);
        doc.Save(writer);
        await writer.FlushAsync();

        Console.WriteLine($"Saved RSS feed: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to fetch series {podcast.Urn}: {ex.Message}");
    }
    finally
    {
        semaphore.Release();
    }
}).ToArray();

await Task.WhenAll(tasks!);
Console.WriteLine("All podcast feeds fetched.");

static string MapToPodcastCategory(string category)
{
    return category switch
    {
        "Dokumentar" => "Documentary",
        "Historie" => "History",
        "Sundhed" => "Health & Fitness",
        "Samfund" => "Society & Culture",
        "Videnskab og tech" => "Science",
        "Tro og eksistens" => "Religion & Spirituality",
        "Kriminal" => "True Crime",
        "Kultur" => "Society & Culture",
        "Nyheder" => "News",
        "Underholdning" => "Entertainment",
        "Sport" => "Sports",
        "Musik" => "Music",
        // Add more mappings as needed
        _ => category // fallback to original if not mapped
    };
}

static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
{
    if (imageAssets is not { Count: > 0 }) return null;

    var img = imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "podcast", StringComparison.OrdinalIgnoreCase) && a?.Ratio == "1:1") ??
              imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "default", StringComparison.OrdinalIgnoreCase) && a?.Ratio == "1:1") ??
              imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "podcast", StringComparison.OrdinalIgnoreCase)) ??
              imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "default", StringComparison.OrdinalIgnoreCase));

    return img?.Id is { } imgId && !string.IsNullOrEmpty(imgId)
        ? $"https://asset.dr.dk/drlyd/images/{imgId}"
        : null;
}

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => !msg.IsSuccessStatusCode)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
                Console.WriteLine($"Retry {retryCount} after {timespan} seconds delay"));

static async Task<List<Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient)
{
    var allEpisodes = new List<Episode>();
    string? nextUrl = initialUrl;

    while (!string.IsNullOrEmpty(nextUrl))
    {
        var response = await httpClient.GetAsync(nextUrl);
        response.EnsureSuccessStatusCode();

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var episodes = JsonSerializer.Deserialize(items.GetRawText(), PodcastJsonContext.Default.ListEpisode);
            if (episodes != null)
                allEpisodes.AddRange(episodes);
        }

        nextUrl = root.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
            ? nextProp.GetString()
            : null;
    }

    return allEpisodes;
}

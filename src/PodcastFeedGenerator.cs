using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Scriban;
using System.Xml.Linq;
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

var tasks = podcastList?.Podcasts.Select(async podcast =>
{
    string urn = podcast.Urn;
    string slug = podcast.Slug;
    string seriesUrl = apiUrl + urn;
    try
    {
        using var httpClient = httpClientFactory.CreateClient("DrApi");

        // First, fetch series information
        var seriesResponse = await httpClient.GetAsync(seriesUrl);
        seriesResponse.EnsureSuccessStatusCode();
        string seriesContent = await seriesResponse.Content.ReadAsStringAsync();
        var series = JsonSerializer.Deserialize(seriesContent, PodcastJsonContext.Default.Series);

        // Fetch all episodes, handling pagination
        var episodes = await FetchAllEpisodesAsync(apiUrl + urn + "/episodes?limit=256", httpClient);

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

        // Prepare Scriban context
        var categories = (series?.Categories ?? new List<string>()).Where(c => !string.IsNullOrEmpty(c)).ToList();
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

        int? seasonCount = null;
        if (series?.GroupingType?.Contains("Seasons", StringComparison.OrdinalIgnoreCase) == true)
        {
            seasonCount = series.NumberOfSeasons > 0 ? series.NumberOfSeasons : series.NumberOfSeries;
            if (seasonCount <= 0) seasonCount = null;
        }

        var channelContext = new
        {
            Title = channelModel.Title,
            Link = channelModel.Link,
            Description = channelModel.Description,
            Language = channelModel.Language,
            Copyright = channelModel.Copyright,
            LastBuildDate = channelModel.LastBuildDate,
            Explicit = channelModel.Explicit,
            Author = channelModel.Author,
            Block = channelModel.Block,
            Owner = channelModel.Owner,
            NewFeedUrl = channelModel.NewFeedUrl,
            Image = channelModel.Image,
            Categories = categories,
            Subtitle = series?.Punchline,
            Summary = series?.Description,
            Type = itunesType,
            Season = seasonCount
        };

        var sortedEpisodes = (episodes ?? new List<Episode>());
        if (itunesType == "serial")
            sortedEpisodes = sortedEpisodes.OrderBy(ep => DateTime.TryParse(ep.PublishTime, out var dt) ? dt : DateTime.MinValue).ToList();
        else
            sortedEpisodes = sortedEpisodes.OrderByDescending(ep => DateTime.TryParse(ep.PublishTime, out var dt) ? dt : DateTime.MinValue).ToList();

        var episodeContexts = sortedEpisodes.Select(episode =>
        {
            string epImage = GetImageUrlFromAssets(episode.ImageAssets) ?? channelModel.Image ?? "";
            var (epAudio, epAudioLength) = episode.AudioAssets?
                .Where(a => a?.Format == "mp3")
                .OrderByDescending(a => a?.Bitrate ?? 0)
                .FirstOrDefault() is { } best
                ? (best.Url ?? "", best.FileSize ?? 0)
                : ("", 0);

            string itunesDuration = episode.DurationMilliseconds > 0
                ? TimeSpan.FromMilliseconds(episode.DurationMilliseconds.Value).ToString(@"hh\:mm\:ss")
                : "";

            return new
            {
                Title = episode.Title ?? "",
                Description = episode.Description ?? "",
                PublishTime = DateTime.TryParse(episode.PublishTime, out var dt)
                    ? dt.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)
                    : episode.PublishTime ?? "",
                ExplicitContent = episode.ExplicitContent,
                Image = epImage,
                Duration = itunesDuration,
                EpisodeNumber = episode.EpisodeNumber,
                SeasonNumber = episode.SeasonNumber,
                PresentationUrl = episode.PresentationUrl,
                Id = episode.Id ?? Guid.NewGuid().ToString(),
                AudioUrl = epAudio,
                AudioLength = epAudioLength,
                Categories = episode.Categories ?? new List<string>()
            };
        }).ToList();

        // Load and render Scriban template
        var templateText = await File.ReadAllTextAsync("src/PodcastFeedTemplate.sbn");
        var template = Template.Parse(templateText);
        var scribanContext = new { channel = channelContext, episodes = episodeContexts };
        var result = template.Render(scribanContext, member => member.Name);

        string outputPath = Path.Combine("output", $"{slug}.xml");
        await File.WriteAllTextAsync(outputPath, result);
        Console.WriteLine($"Saved RSS feed: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to fetch series {urn}: {ex.Message}");
    }
}).ToArray();

await Task.WhenAll(tasks!);
Console.WriteLine("All podcast feeds fetched.");

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

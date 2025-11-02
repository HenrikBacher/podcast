using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace DrPodcast;

public interface IPodcastFeedService
{
    Task GenerateAllFeedsAsync();
}

public class PodcastFeedService : IPodcastFeedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PodcastFeedService> _logger;
    private readonly string _baseUrl;
    private const string ApiUrl = "https://api.dr.dk/radio/v2/series/";

    public PodcastFeedService(
        IHttpClientFactory httpClientFactory,
        ILogger<PodcastFeedService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
    }

    public async Task GenerateAllFeedsAsync()
    {
        _logger.LogInformation("Starting feed generation");

        // Deserialize podcasts.json using JsonSerializerContext for trimming compatibility
        var podcastList = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync("podcasts.json"), PodcastJsonContext.Default.PodcastList);

        if (podcastList?.Podcasts == null || podcastList.Podcasts.Count == 0)
        {
            _logger.LogWarning("No podcasts found in podcasts.json");
            return;
        }

        Directory.CreateDirectory("output");

        var tasks = podcastList.Podcasts.Select(async podcast =>
        {
            try
            {
                await GenerateFeedForPodcastAsync(podcast);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate feed for podcast {Urn}", podcast.Urn);
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        _logger.LogInformation("All podcast feeds generated");
    }

    private async Task GenerateFeedForPodcastAsync(Podcast podcast)
    {
        string urn = podcast.Urn;
        string slug = podcast.Slug;
        string seriesUrl = ApiUrl + urn;

        _logger.LogInformation("Generating feed for podcast: {Slug} (URN: {Urn})", slug, urn);

        using var httpClient = _httpClientFactory.CreateClient("DrApi");

        // First, fetch series information
        var seriesResponse = await httpClient.GetAsync(seriesUrl);
        seriesResponse.EnsureSuccessStatusCode();
        string seriesContent = await seriesResponse.Content.ReadAsStringAsync();
        var series = JsonSerializer.Deserialize(seriesContent, PodcastJsonContext.Default.Series);

        // Fetch all episodes, handling pagination
        var episodes = await FetchAllEpisodesAsync(ApiUrl + urn + "/episodes?limit=256", httpClient);

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
            NewFeedUrl = $"{_baseUrl}/feeds/{slug}.xml",
            Image = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets) ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets)
        };

        // XML namespaces for RSS/iTunes
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        XNamespace media = "http://search.yahoo.com/mrss/";

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", $"{_baseUrl}/feeds/{slug}.xml"),
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

        // Add iTunes categories from series
        if (series?.Categories is { Count: > 0 } categories)
        {
            foreach (var category in categories.Where(c => !string.IsNullOrEmpty(c)))
            {
                var mapped = PodcastHelpers.MapToPodcastCategory(category);
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
                string? epImage = PodcastHelpers.GetImageUrlFromAssets(episode.ImageAssets) ?? channelModel.Image;

                // Select the highest quality mp3 using LINQ and pattern matching
                var (epAudio, epAudioLength) = episode.AudioAssets?
                    .Where(a => a?.Format == "mp3")
                    .OrderByDescending(a => a?.Bitrate ?? 0)
                    .FirstOrDefault() is { } best
                    ? (best.Url ?? "", best.FileSize ?? 0)
                    : ("", 0);

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
                        var mapped = PodcastHelpers.MapToPodcastCategory(cat);
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
        doc.Save(outputPath);
        _logger.LogInformation("Saved RSS feed: {OutputPath}", outputPath);
    }

    private async Task<List<Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient)
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
}

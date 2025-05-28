using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using DrPodcast;

class Program
{
    static async Task Main(string[] args)
    {
        // Configure services with HttpClientFactory and Polly
        var services = new ServiceCollection();
        
        // Get apiKey from environment variables, fallback to defaults if not set
        string apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
        
        // Configure HttpClient with Polly retry policy
        services.AddHttpClient("DrApi", client =>
        {
            client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(GetRetryPolicy());

        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        // Deserialize podcasts.json using strongly typed models
        string podcastsJson = File.ReadAllText("podcasts.json");
        var podcastList = System.Text.Json.JsonSerializer.Deserialize<global::DrPodcast.PodcastList>(podcastsJson);
        // Get baseUrl from environment variables, fallback to defaults if not set
        string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
        string episodesApiUrl = "https://api.dr.dk/radio/v2/series/";
        string seriesApiUrl = "https://api.dr.dk/radio/v2/series/";

        Directory.CreateDirectory("output");

        var tasks = podcastList?.Podcasts.Select(async (global::DrPodcast.Podcast podcast) =>
        {
            string urn = podcast.Urn;
            string slug = podcast.Slug;
            string episodesUrl = episodesApiUrl + urn + "/episodes?limit=1024";
            string seriesUrl = seriesApiUrl + urn;
            try
            {
                using var httpClient = httpClientFactory.CreateClient("DrApi");
                
                // First, fetch series information
                var seriesResponse = await httpClient.GetAsync(seriesUrl);
                seriesResponse.EnsureSuccessStatusCode();
                string seriesContent = await seriesResponse.Content.ReadAsStringAsync();
                var series = System.Text.Json.JsonSerializer.Deserialize<global::DrPodcast.Series>(seriesContent);

                // Fetch all episodes, handling pagination
                var episodes = await FetchAllEpisodesAsync(episodesApiUrl + urn + "/episodes?limit=256", httpClient);

                // Build strongly typed channel model using rich series data
                var channelModel = new global::DrPodcast.Channel
                {
                    Title = series?.Title + " (Reproduceret feed)",
                    Link = series?.PresentationUrl ?? $"https://www.dr.dk/lyd/special-radio/{slug}",
                    Description = series?.Description ?? series?.Punchline ?? $"Feed for {slug}",
                    Language = "da",
                    Copyright = "DR",
                    LastBuildDate = DateTime.TryParse(series?.LatestEpisodeStartTime, out var lastEpisode)
                        ? lastEpisode.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture)
                        : DateTime.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture),
                    Explicit = series?.ExplicitContent == true ? "yes" : "no",
                    Author = "DR",
                    Block = "yes",
                    Owner = new global::DrPodcast.ChannelOwner { Email = "podcast@dr.dk", Name = "DR" },
                    NewFeedUrl = $"{baseUrl}/{slug}.xml",
                    Image = GetImageUrlFromAssets(series?.ImageAssets) ?? GetImageUrlFromAssets(podcast.ImageAssets)
                };

                // Add required namespaces for iTunes and media
                XNamespace atom = "http://www.w3.org/2005/Atom";
                XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
                XNamespace media = "http://search.yahoo.com/mrss/";

                var channel = new XElement("channel",
                    new XElement(atom + "link",
                        new XAttribute("href", $"{baseUrl}/{slug}.xml"),
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

                // Add iTunes categories from series data
                if (series?.Categories != null && series.Categories.Count > 0)
                {
                    foreach (var category in series.Categories)
                    {
                        if (!string.IsNullOrEmpty(category))
                        {
                            channel.Add(new XElement(itunes + "category", new XAttribute("text", category)));
                        }
                    }
                }

                // Add series summary/subtitle if available
                if (!string.IsNullOrEmpty(series?.Punchline))
                {
                    channel.Add(new XElement(itunes + "subtitle", series.Punchline));
                }

                // Add series summary
                if (!string.IsNullOrEmpty(series?.Description))
                {
                    channel.Add(new XElement(itunes + "summary", series.Description));
                }

                if (episodes != null)
                {
                    foreach (global::DrPodcast.Episode ep in episodes)
                    {
                        string epTitle = ep.Title ?? "";
                        string epDesc = ep.Description ?? "";
                        string epPubDate = ep.PublishTime ?? "";
                        string epGuid = ep.Id ?? Guid.NewGuid().ToString();
                        string epLink = ep.PresentationUrl ?? "";
                        string epExplicit = "no";
                        string epAuthor = "DR";
                        string epCountry = "dk";
                        string? epImage = GetImageUrlFromAssets(ep.ImageAssets) ?? channelModel.Image;

                        // Select the highest quality mp3 file available from audioAssets
                        string epAudio = "";
                        int epAudioLength = 0;
                        int epAudioDurationMs = ep.DurationMilliseconds ?? 0;
                        string itunesDuration = "";
                        var audioAssets = ep.AudioAssets;
                        if (audioAssets != null && audioAssets.Count > 0)
                        {
                            var mp3Assets = audioAssets
                                .Where(a => a?.Format == "mp3")
                                .OrderByDescending(a => a?.Bitrate ?? 0)
                                .ToList();
                            if (mp3Assets.Count > 0)
                            {
                                var best = mp3Assets[0];
                                epAudio = best?.Url ?? "";
                                epAudioLength = best?.FileSize ?? 0;
                            }
                        }
                        // Format itunes:duration as HH:mm:ss
                        if (epAudioDurationMs > 0)
                        {
                            var ts = TimeSpan.FromMilliseconds(epAudioDurationMs);
                            itunesDuration = ts.ToString(@"hh\:mm\:ss");
                        }

                        var item = new XElement("item",
                            new XElement("title", epTitle),
                            new XElement("description", epDesc),
                            new XElement("pubDate", DateTime.TryParse(epPubDate, out var dt) ? dt.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) : epPubDate),
                            new XElement("explicit", epExplicit),
                            new XElement(itunes + "author", epAuthor),
                            new XElement(itunes + "image", epImage),
                            new XElement(itunes + "duration", itunesDuration),
                            new XElement(media + "restriction",
                                new XAttribute("relationship", "allow"),
                                new XAttribute("type", "country"),
                                epCountry
                            )
                        );
                        if (!string.IsNullOrEmpty(epLink))
                            item.Add(new XElement("link", epLink));
                        if (!string.IsNullOrEmpty(epGuid))
                            item.Add(new XElement("guid", new XAttribute("isPermalink", "false"), epGuid));
                        if (!string.IsNullOrEmpty(epAudio))
                        {
                            var enclosure = new XElement("enclosure",
                                new XAttribute("url", epAudio),
                                new XAttribute("type", "audio/mpeg")
                            );
                            if (epAudioLength > 0)
                                enclosure.Add(new XAttribute("length", epAudioLength));
                            item.Add(enclosure);
                        }
                        // Propagate categories from upstream feed if present
                        var categories = ep.Categories;
                        if (categories != null && categories.Count > 0)
                        {
                            foreach (var cat in categories)
                            {
                                if (!string.IsNullOrEmpty(cat))
                                    item.Add(new XElement("category", cat));
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
                Console.WriteLine($"Saved RSS feed: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch series {urn}: {ex.Message}");
            }
        }).ToArray();

        await Task.WhenAll(tasks!);
        Console.WriteLine("All podcast feeds fetched.");
    }

    static string? GetImageUrlFromAssets(List<global::DrPodcast.ImageAsset>? imageAssets)
    {
        if (imageAssets != null && imageAssets.Count > 0)
        {
            // Prefer target 'podcast' with ratio '1:1', then 'default' with ratio '1:1', then any 'podcast', then any 'default'
            var img = imageAssets.FirstOrDefault(a => a?.Target?.ToLower() == "podcast" && a?.Ratio == "1:1");
            if (img == null)
                img = imageAssets.FirstOrDefault(a => a?.Target?.ToLower() == "default" && a?.Ratio == "1:1");
            if (img == null)
                img = imageAssets.FirstOrDefault(a => a?.Target?.ToLower() == "podcast");
            if (img == null)
                img = imageAssets.FirstOrDefault(a => a?.Target?.ToLower() == "default");
            if (img != null)
            {
                var imgId = img.Id;
                if (!string.IsNullOrEmpty(imgId))
                {
                    return $"https://asset.dr.dk/drlyd/images/{imgId}";
                }
            }
        }
        return null;
    }

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => !msg.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Console.WriteLine($"Retry {retryCount} after {timespan} seconds delay");
                });
    }

    // Helper method to fetch all episodes with pagination
    static async Task<List<global::DrPodcast.Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient)
    {
        var allEpisodes = new List<global::DrPodcast.Episode>();
        string? nextUrl = initialUrl;
        while (!string.IsNullOrEmpty(nextUrl))
        {
            var response = await httpClient.GetAsync(nextUrl);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var episodes = System.Text.Json.JsonSerializer.Deserialize<List<global::DrPodcast.Episode>>(items.GetRawText());
                if (episodes != null)
                    allEpisodes.AddRange(episodes);
            }
            // Check for next property
            if (root.TryGetProperty("next", out var nextProp) && nextProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                nextUrl = nextProp.GetString();
            }
            else
            {
                nextUrl = null;
            }
        }
        return allEpisodes;
    }
}

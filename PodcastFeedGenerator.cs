using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DrPodcast;

class Program
{
    static async Task Main(string[] args)
    {
        // Deserialize podcasts.json using strongly typed models
        string podcastsJson = File.ReadAllText("podcasts.json");
        var podcastList = System.Text.Json.JsonSerializer.Deserialize<global::DrPodcast.PodcastList>(podcastsJson);
        // Get baseUrl and apiKey from environment variables, fallback to defaults if not set
        string baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
        string apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
        string episodesApiUrl = "https://api.dr.dk/radio/v2/series/";
        string seriesApiUrl = "https://api.dr.dk/radio/v2/series/";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("X-Apikey", apiKey);

        Directory.CreateDirectory("output");

        var tasks = podcastList?.Podcasts.Select(async (global::DrPodcast.Podcast podcast) =>
        {
            string urn = podcast.Urn;
            string slug = podcast.Slug;
            string episodesUrl = episodesApiUrl + urn + "/episodes?limit=1024";
            string seriesUrl = seriesApiUrl + urn;
            try
            {
                // First, fetch series information
                var seriesResponse = await httpClient.GetAsync(seriesUrl);
                seriesResponse.EnsureSuccessStatusCode();
                string seriesContent = await seriesResponse.Content.ReadAsStringAsync();
                var series = System.Text.Json.JsonSerializer.Deserialize<global::DrPodcast.Series>(seriesContent);

                // Then fetch episodes
                var episodesResponse = await httpClient.GetAsync(episodesUrl);
                episodesResponse.EnsureSuccessStatusCode();
                string episodesContent = await episodesResponse.Content.ReadAsStringAsync();
                // Deserialize episodes using strongly typed models
                var feedDoc = System.Text.Json.JsonDocument.Parse(episodesContent);
                var itemsProp = feedDoc.RootElement.TryGetProperty("items", out var items) ? items : default;
                var episodes = itemsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? System.Text.Json.JsonSerializer.Deserialize<List<global::DrPodcast.Episode>>(itemsProp.GetRawText())
                    : null;

                // Build strongly typed channel model using rich series data
                var channelModel = new global::DrPodcast.Channel
                {
                    Title = series?.Title ?? slug,
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
                    Owner = new global::DrPodcast.ChannelOwner { Email = "podcast@dr.dk", Name = series?.Channel?.Title ?? "DR" },
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
                        // Select episode image from imageAssets array if available
                        var episodeImageUrl = GetImageUrlFromAssets(ep.ImageAssets);
                        if (!string.IsNullOrEmpty(episodeImageUrl))
                        {
                            item.Add(new XElement(media + "thumbnail", new XAttribute("url", episodeImageUrl)));
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
}

namespace DrPodcast;

public sealed class FeedGenerationService(IHttpClientFactory httpClientFactory, ILogger<FeedGenerationService> logger)
{
    private const string ApiUrl = "https://api.dr.dk/radio/v2/series/";
    private const string Rfc822Format = "ddd, dd MMM yyyy HH:mm:ss zzz";

    public async Task GenerateFeedsAsync(string podcastsJsonPath, string baseUrl, GeneratorConfig config, CancellationToken cancellationToken = default)
    {
        var podcastList = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(podcastsJsonPath, cancellationToken),
            PodcastJsonContext.Default.PodcastList);

        if (podcastList is null || podcastList.Podcasts.Count == 0)
        {
            logger.LogWarning("No podcasts found in {Path}", podcastsJsonPath);
            return;
        }

        Directory.CreateDirectory(config.FeedsDir);

        var tasks = podcastList.Podcasts.Select(podcast =>
            ProcessPodcastAsync(podcast, baseUrl, config, cancellationToken)).ToArray();

        var results = await Task.WhenAll(tasks);
        var feedMetadata = results.OfType<FeedMetadata>().ToList();

        logger.LogInformation("Generated {Count} podcast feeds.", feedMetadata.Count);

        await WebsiteGenerator.GenerateAsync(feedMetadata, config);
    }

    private async Task<FeedMetadata?> ProcessPodcastAsync(Podcast podcast, string baseUrl, GeneratorConfig config, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("DrApi");

            var seriesResponse = await httpClient.GetAsync($"{ApiUrl}{podcast.Urn}", cancellationToken);
            seriesResponse.EnsureSuccessStatusCode();

            await using var stream = await seriesResponse.Content.ReadAsStreamAsync(cancellationToken);
            var series = await JsonSerializer.DeserializeAsync(
                stream,
                PodcastJsonContext.Default.Series,
                cancellationToken);

            // Skip regeneration if the feed is already up-to-date, so Last-Modified stays stable
            string outputPath = Path.Combine(config.FeedsDir, $"{podcast.Slug}.xml");
            if (File.Exists(outputPath) && !HasNewerEpisodes(outputPath, series))
            {
                logger.LogInformation("Skipped {Slug} (unchanged)", podcast.Slug);
                var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets)
                               ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets);
                var title = series?.Title ?? podcast.Slug.Replace("-", " ");
                var cleanTitle = RegexCache.FeedTitleCleanup().Replace(title, "").Trim();
                return new FeedMetadata(podcast.Slug, cleanTitle, imageUrl);
            }

            var episodes = await FetchAllEpisodesAsync($"{ApiUrl}{podcast.Urn}/episodes?limit=256", httpClient, cancellationToken);

            var (rss, metadata) = BuildRssFeed(series, episodes, podcast, baseUrl, config.PreferMp4);

            // Atomic write: write to temp file then rename to avoid serving partial files
            string tempPath = outputPath + ".tmp";
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(fileStream, new UTF8Encoding(false)))
            {
                var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);
                xmlDoc.Save(writer);
            }
            File.Move(tempPath, outputPath, overwrite: true);

            logger.LogInformation("Generated {Slug}", podcast.Slug);

            return metadata;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Urn}", podcast.Urn);
            return null;
        }
    }

    private static (XElement rss, FeedMetadata metadata) BuildRssFeed(Series? series, List<Episode>? episodes, Podcast podcast, string baseUrl, bool preferMp4 = false)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets)
                       ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets);

        // Use the latest episode start time — avoid DateTime.Now so feed content
        // stays stable across regenerations when nothing has changed (preserves ETags).
        var lastBuildDate = DateTime.TryParse(series?.LatestEpisodeStartTime, out var dt)
            ? dt.ToString(Rfc822Format, CultureInfo.InvariantCulture)
            : null;

        var itunesType = DetermineItunesType(series);

        var title = series?.Title ?? podcast.Slug.Replace("-", " ");
        var cleanTitle = RegexCache.FeedTitleCleanup().Replace(title, "").Trim();

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", $"{baseUrl.TrimEnd('/')}/feeds/{podcast.Slug}.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")),
            new XElement("title", series?.Title),
            new XElement("link", series?.PresentationUrl),
            new XElement("description", series?.Description),
            new XElement("language", "da"),
            new XElement("copyright", "DR"),
            new XElement(itunes + "explicit", series?.ExplicitContent == true ? "yes" : "no"),
            new XElement(itunes + "author", "DR"),
            new XElement(itunes + "block", "yes"),
            new XElement(itunes + "owner",
                new XElement(itunes + "email", "podcast@dr.dk"),
                new XElement(itunes + "name", "DR")),
            new XElement(itunes + "type", itunesType));

        if (!string.IsNullOrEmpty(lastBuildDate))
        {
            channel.Add(new XElement("lastBuildDate", lastBuildDate));
        }

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
                sorted = series?.DefaultOrder == "Asc"
                    ? episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenBy(e => e.Order ?? long.MaxValue)
                    : episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenByDescending(e => e.Order ?? long.MinValue);
            }
            else
            {
                sorted = series?.DefaultOrder == "Asc"
                    ? episodes.OrderBy(e => e.Order ?? long.MaxValue)
                    : episodes.OrderByDescending(e => e.Order ?? long.MinValue);
            }

            foreach (var episode in sorted)
            {
                channel.Add(BuildEpisodeItem(episode, imageUrl, baseUrl, preferMp4, itunes));
            }
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            channel);

        var metadata = new FeedMetadata(podcast.Slug, cleanTitle, imageUrl);

        return (rss, metadata);
    }

    private static bool HasNewerEpisodes(string feedPath, Series? series)
    {
        if (!DateTime.TryParse(series?.LatestEpisodeStartTime, out var latestEpisode))
            return true; // Can't determine, regenerate to be safe

        try
        {
            var doc = XDocument.Load(feedPath);
            var lastBuildDate = doc.Root?.Element("channel")?.Element("lastBuildDate")?.Value;
            if (lastBuildDate is null || !DateTime.TryParseExact(lastBuildDate, Rfc822Format,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var existing))
                return true;

            return latestEpisode > existing;
        }
        catch
        {
            return true; // Corrupt/unreadable file, regenerate
        }
    }

    private static string DetermineItunesType(Series? series) =>
        series?.PresentationType == "Show" ? "serial" : "episodic";

    private static void AddCategories(XElement element, List<string>? categories, XNamespace itunes)
    {
        if (categories is null) return;

        foreach (var category in categories)
        {
            if (string.IsNullOrEmpty(category)) continue;
            element.Add(new XElement(itunes + "category", new XAttribute("text", category)));
        }
    }

    private static XElement BuildEpisodeItem(Episode episode, string? channelImage, string baseUrl, bool preferMp4, XNamespace itunes)
    {
        AudioAsset? audioAsset;
        if (preferMp4)
        {
            audioAsset = episode.AudioAssets?
                             .Where(a => a?.Format is "mp4" or "m4a")
                             .MaxBy(a => a?.Bitrate ?? 0)
                         ?? episode.AudioAssets?
                             .Where(a => a?.Format == "mp3")
                             .MaxBy(a => a?.Bitrate ?? 0);
        }
        else
        {
            audioAsset = episode.AudioAssets?
                .Where(a => a?.Format == "mp3")
                .MaxBy(a => a?.Bitrate ?? 0);
        }

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
            new XElement(itunes + "explicit", episode.ExplicitContent ? "yes" : "no"),
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
            var needsProxy = preferMp4 && audioAsset.Format is "mp4" or "m4a";
            Uri.TryCreate(url, UriKind.Absolute, out var audioUri);
            var canProxy = needsProxy
                && !string.IsNullOrEmpty(baseUrl)
                && audioUri is { Scheme: "https" }
                && audioUri.Host.EndsWith(".dr.dk", StringComparison.OrdinalIgnoreCase);
            var enclosureUrl = canProxy
                ? $"{baseUrl.TrimEnd('/')}/proxy/audio?path={Uri.EscapeDataString(audioUri!.PathAndQuery)}"
                : url;
            var mimeType = canProxy ? "audio/mp4" : GetMimeTypeFromFormat(audioAsset.Format);
            var enclosure = new XElement("enclosure",
                new XAttribute("url", enclosureUrl),
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

    private static string GetMimeTypeFromFormat(string? format)
    {
        if (format is null) return "audio/mpeg";

        return format.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "audio/mpeg" :
               format.Equals("mp4", StringComparison.OrdinalIgnoreCase) ? "audio/mp4" :
               format.Equals("aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" :
               format.Equals("m4a", StringComparison.OrdinalIgnoreCase) ? "audio/mp4" :
               format.Equals("ogg", StringComparison.OrdinalIgnoreCase) ? "audio/ogg" :
               format.Equals("wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" :
               format.Equals("flac", StringComparison.OrdinalIgnoreCase) ? "audio/flac" :
               "audio/mpeg";
    }

    private static async Task<List<Episode>?> FetchAllEpisodesAsync(string initialUrl, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var limitMatch = Regex.Match(initialUrl, @"limit=(\d+)");
        var estimatedCapacity = limitMatch.Success ? int.Parse(limitMatch.Groups[1].Value) : 256;
        List<Episode> allEpisodes = new(estimatedCapacity);

        string? nextUrl = initialUrl;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            var response = await httpClient.GetAsync(nextUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
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
}

internal static partial class RegexCache
{
    [GeneratedRegex(@"\s*\([^)]*feed[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex FeedTitleCleanup();
}

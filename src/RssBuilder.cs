namespace DrPodcast;

public static class RssBuilder
{
    internal const string Rfc822Format = "ddd, dd MMM yyyy HH:mm:ss zzz";

    /// <summary>Format a DateTime as RFC 822 with compact timezone offset (+0000 instead of +00:00).</summary>
    public static string FormatRfc822(DateTime dt)
    {
        var s = dt.ToString(Rfc822Format, CultureInfo.InvariantCulture);
        var i = s.LastIndexOf(':');
        return string.Concat(s.AsSpan(0, i), s.AsSpan(i + 1));
    }

    public static string DetermineItunesType(Series? series) =>
        series?.PresentationType == "Show" ? "serial" : "episodic";

    public static FeedMetadata BuildFeedMetadata(Podcast podcast, Series? series)
    {
        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets)
                       ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets);
        var title = series?.Title ?? podcast.Slug.Replace("-", " ");
        var cleanTitle = RegexCache.FeedTitleCleanup().Replace(title, "").Trim();
        return new FeedMetadata(podcast.Slug, cleanTitle, imageUrl);
    }

    public static (XElement rss, FeedMetadata metadata) BuildRssFeed(Series? series, List<Episode>? episodes, Podcast podcast, string baseUrl, bool preferMp4 = false)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var metadata = BuildFeedMetadata(podcast, series);

        // Use the latest episode start time — avoid DateTime.Now so feed content
        // stays stable across regenerations when nothing has changed (preserves ETags).
        var lastBuildDate = DateTime.TryParse(series?.LatestEpisodeStartTime, out var dt)
            ? FormatRfc822(dt)
            : null;

        var itunesType = DetermineItunesType(series);

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", $"{baseUrl.TrimEnd('/')}/feeds/{podcast.Slug}.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/xml")),
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

        if (!string.IsNullOrEmpty(metadata.ImageUrl))
        {
            channel.Add(new XElement(itunes + "image", new XAttribute("href", metadata.ImageUrl)));
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
            var hasSeasons = series?.NumberOfSeries > 0;
            var ascending = series?.DefaultOrder == "Asc";

            IOrderedEnumerable<Episode> sorted = (hasSeasons, ascending) switch
            {
                (true,  true)  => episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenBy(e => e.Order ?? long.MaxValue),
                (true,  false) => episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenByDescending(e => e.Order ?? long.MinValue),
                (false, true)  => episodes.OrderBy(e => e.Order ?? long.MaxValue),
                (false, false) => episodes.OrderByDescending(e => e.Order ?? long.MinValue),
            };

            foreach (var episode in sorted)
            {
                channel.Add(BuildEpisodeItem(episode, metadata.ImageUrl, baseUrl, preferMp4, itunes));
            }
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            channel);

        return (rss, metadata);
    }

    public static AudioAsset? SelectAudioAsset(Episode episode, bool preferMp4)
    {
        if (preferMp4)
        {
            return episode.AudioAssets?
                       .Where(a => a?.Format is "mp4" or "m4a")
                       .MaxBy(a => a?.Bitrate ?? 0)
                   ?? episode.AudioAssets?
                       .Where(a => a?.Format == "mp3")
                       .MaxBy(a => a?.Bitrate ?? 0);
        }

        return episode.AudioAssets?
            .Where(a => a?.Format == "mp3")
            .MaxBy(a => a?.Bitrate ?? 0);
    }

    public static XElement BuildEpisodeItem(Episode episode, string? channelImage, string baseUrl, bool preferMp4, XNamespace itunes)
    {
        var audioAsset = SelectAudioAsset(episode, preferMp4);

        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(episode.ImageAssets) ?? channelImage;
        var duration = episode.DurationMilliseconds is not null
            ? TimeSpan.FromMilliseconds(episode.DurationMilliseconds.Value).ToString(@"hh\:mm\:ss")
            : "";

        var rawTime = episode.StartTime ?? episode.PublishTime;
        var pubDate = DateTime.TryParse(rawTime, out var dt)
            ? FormatRfc822(dt)
            : rawTime ?? "";

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
            var assetMatch = audioUri is not null ? RegexCache.DrAssetUrl().Match(audioUri.PathAndQuery) : Match.Empty;
            var canProxy = needsProxy
                && !string.IsNullOrEmpty(baseUrl)
                && audioUri is { Scheme: "https" }
                && audioUri.Host.EndsWith(".dr.dk", StringComparison.OrdinalIgnoreCase)
                && assetMatch.Success;
            var enclosureUrl = canProxy
                ? $"{baseUrl.TrimEnd('/')}/proxy/audio/{assetMatch.Groups["ep"].Value}/{assetMatch.Groups["asset"].Value}"
                : url;
            var mimeType = needsProxy ? "audio/mp4" : GetMimeTypeFromFormat(audioAsset.Format);
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

    private static void AddCategories(XElement element, List<string>? categories, XNamespace itunes)
    {
        if (categories is null) return;

        foreach (var category in categories)
        {
            if (string.IsNullOrEmpty(category)) continue;
            element.Add(new XElement(itunes + "category", new XAttribute("text", category)));
        }
    }

    private static string GetMimeTypeFromFormat(string? format) =>
        format?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "mp4" or "m4a" => "audio/mp4",
            "aac" => "audio/aac",
            "ogg" => "audio/ogg",
            "wav" => "audio/wav",
            "flac" => "audio/flac",
            _ => "audio/mpeg"
        };
}

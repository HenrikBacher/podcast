namespace DrPodcast;

public static class RssBuilder
{
    internal const string Rfc822Format = "ddd, dd MMM yyyy HH:mm:ss zzz";
    internal const DateTimeStyles UtcParseStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>Format a DateTime as RFC 822 with compact timezone offset (+0000 instead of +00:00).</summary>
    public static string FormatRfc822(DateTime dt)
    {
        var s = dt.ToString(Rfc822Format, CultureInfo.InvariantCulture);
        var i = s.LastIndexOf(':');
        return string.Concat(s.AsSpan(0, i), s.AsSpan(i + 1));
    }

    internal static bool TryParseFeedDate(string? raw, out DateTime utc) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, UtcParseStyles, out utc);

    public static string DetermineItunesType(Series? series) =>
        series?.PresentationType == "Show" ? "serial" : "episodic";

    public static FeedMetadata BuildFeedMetadata(Podcast podcast, Series? series)
    {
        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets)
                       ?? PodcastHelpers.GetImageUrlFromAssets(podcast.ImageAssets);
        var title = series?.Title ?? podcast.Slug.Replace("-", " ");
        return new FeedMetadata(podcast.Slug, StripTrailingFeedParenthetical(title), imageUrl);
    }

    // Strips a trailing parenthetical that mentions "feed", e.g. "Show name (RSS feed)" → "Show name".
    internal static string StripTrailingFeedParenthetical(string title)
    {
        var trimmed = title.AsSpan().TrimEnd();
        if (trimmed.Length == 0 || trimmed[^1] != ')') return title.Trim();

        var open = trimmed.LastIndexOf('(');
        if (open < 0) return title.Trim();

        var inside = trimmed[(open + 1)..^1];
        if (inside.Contains("feed", StringComparison.OrdinalIgnoreCase))
            return trimmed[..open].TrimEnd().ToString();

        return title.Trim();
    }

    public static (XElement rss, FeedMetadata metadata) BuildRssFeed(Series? series, List<Episode>? episodes, Podcast podcast, string baseUrl)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var metadata = BuildFeedMetadata(podcast, series);

        // Use the latest episode start time — avoid DateTime.Now so feed content
        // stays stable across regenerations when nothing has changed (preserves ETags).
        var lastBuildDate = TryParseFeedDate(series?.LatestEpisodeStartTime, out var dt)
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
                channel.Add(BuildEpisodeItem(episode, metadata.ImageUrl, itunes));
            }
        }

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            channel);

        return (rss, metadata);
    }

    public static AudioAsset? SelectAudioAsset(Episode episode)
    {
        if (episode.AudioAssets is not { } assets)
            return null;

        // Match the format case-insensitively so an upstream "MP3" doesn't silently
        // drop an episode's audio.
        return assets.Where(a => a?.Format?.ToLowerInvariant() == "mp3").MaxBy(a => a?.Bitrate ?? 0);
    }

    public static XElement BuildEpisodeItem(Episode episode, string? channelImage, XNamespace itunes)
    {
        var audioAsset = SelectAudioAsset(episode);

        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(episode.ImageAssets) ?? channelImage;
        var duration = episode.DurationMilliseconds is not null
            ? TimeSpan.FromMilliseconds(episode.DurationMilliseconds.Value).ToString(@"hh\:mm\:ss")
            : "";

        var rawTime = episode.StartTime ?? episode.PublishTime;
        var pubDate = TryParseFeedDate(rawTime, out var dt)
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
            var enclosure = new XElement("enclosure",
                new XAttribute("url", url),
                new XAttribute("type", "audio/mpeg"));

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

}

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

    /// <summary>
    /// A feed's public address. Podcatchers are subscribed to exactly this URL, so it must never change
    /// shape; the feed's atom:self link and every link to the feed on the website take it from here.
    /// </summary>
    public static string FeedUrl(string baseUrl, string slug) => $"{baseUrl.TrimEnd('/')}/feeds/{slug}.xml";

    internal static bool TryParseFeedDate(string? raw, out DateTime utc) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture, UtcParseStyles, out utc);

    /// <summary>
    /// Artwork as feeds reference it: 2048×2048, inside Apple's 1400–3000 px range and well below the size of
    /// DR's 3000×3000 originals (up to ~1.2 MB). The website asks for a smaller size of the same image.
    /// </summary>
    internal static string FeedArtwork(string imageUrl) => PodcastHelpers.WithImageWidth(imageUrl, 2048);

    public static string DetermineItunesType(Series? series) =>
        series?.PresentationType == "Show" ? "serial" : "episodic";

    /// <param name="newestEpisode">The newest episode the feed contains, named on the website.</param>
    public static FeedMetadata BuildFeedMetadata(Podcast podcast, Series? series, Episode? newestEpisode = null)
    {
        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(series?.ImageAssets);
        var title = series?.Title ?? podcast.Slug.Replace("-", " ");
        var categories = series?.Categories?.Where(c => !string.IsNullOrEmpty(c)).ToList();
        return new FeedMetadata(podcast.Slug, StripTrailingFeedParenthetical(title), imageUrl,
            Subtitle: string.IsNullOrEmpty(series?.Punchline) ? null : series.Punchline,
            Description: string.IsNullOrEmpty(series?.Description) ? null : series.Description,
            Categories: categories is { Count: > 0 } ? string.Join('|', categories) : null,
            LatestEpisodeTitle: newestEpisode?.Title,
            LatestEpisodeUtc: TryParseFeedDate(newestEpisode?.StartTime ?? series?.LatestEpisodeStartTime, out var latest) ? latest : null);
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

    public static XElement BuildRssFeed(Series? series, List<Episode>? episodes, Podcast podcast, string baseUrl)
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
                new XAttribute("href", FeedUrl(baseUrl, podcast.Slug)),
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
            channel.Add(new XElement(itunes + "image", new XAttribute("href", FeedArtwork(metadata.ImageUrl))));
        }

        if (!string.IsNullOrEmpty(series?.Punchline))
        {
            channel.Add(new XElement(itunes + "subtitle", series.Punchline));
        }

        if (!string.IsNullOrEmpty(series?.Description))
        {
            channel.Add(new XElement(itunes + "summary", series.Description));
        }

        foreach (var category in series?.Categories ?? [])
        {
            if (!string.IsNullOrEmpty(category))
                channel.Add(new XElement(itunes + "category", new XAttribute("text", category)));
        }

        if (episodes is not null)
        {
            var hasSeasons = series?.HasSeasons == true;
            var ascending = series?.DefaultOrder == "Asc";

            IOrderedEnumerable<Episode> sorted = (hasSeasons, ascending) switch
            {
                (true,  true)  => episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenBy(e => e.Order ?? long.MaxValue),
                (true,  false) => episodes.OrderByDescending(e => e.SeasonNumber ?? int.MinValue).ThenByDescending(e => e.Order ?? long.MinValue),
                (false, true)  => episodes.OrderBy(e => e.Order ?? long.MaxValue),
                (false, false) => episodes.OrderByDescending(e => e.Order ?? long.MinValue),
            };

            foreach (var episode in sorted.Where(HasDownloadableAudio))
            {
                channel.Add(BuildEpisodeItem(episode, metadata.ImageUrl, itunes, channelExplicit: series?.ExplicitContent == true));
            }
        }

        return new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            channel);
    }

    /// <summary>
    /// The feed as written to disk: UTF-8 without a BOM, after an XML declaration. Deterministic for a
    /// given tree, which is what lets an unchanged feed be recognised byte for byte and left alone.
    /// </summary>
    /// <param name="expectedSize">Initial buffer size. Feeds run to a few MB, and growing the buffer by
    /// doubling would leave a trail of discarded Large Object Heap arrays behind it.</param>
    public static ReadOnlyMemory<byte> Serialize(XElement rss, int expectedSize = 0)
    {
        var buffer = new MemoryStream(expectedSize);
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
        {
            new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss).Save(writer);
        }
        return buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
    }

    /// <summary>
    /// Whether an episode belongs in a feed: podcast apps need a file to download. DR offers some
    /// episodes (mostly in music-heavy shows, presumably for rights reasons) only as an HLS stream,
    /// and an item without an enclosure just shows up as an episode that can't be played.
    /// </summary>
    public static bool HasDownloadableAudio(Episode episode) => !string.IsNullOrEmpty(SelectAudioAsset(episode)?.Url);

    public static AudioAsset? SelectAudioAsset(Episode episode)
    {
        if (episode.AudioAssets is not { } assets)
            return null;

        // Match the format case-insensitively so an upstream "MP3" doesn't silently drop an
        // episode's audio. OrdinalIgnoreCase rather than ToLowerInvariant: this runs for every
        // asset of every episode of every podcast, and the lowercased copies are pure garbage.
        return assets
            .Where(a => string.Equals(a?.Format, "mp3", StringComparison.OrdinalIgnoreCase))
            .MaxBy(a => a?.Bitrate ?? 0);
    }

    /// <summary>
    /// An episode's &lt;item&gt;. What it shares with the channel (author, explicit flag, artwork) is left
    /// out: apps fall back to the channel's, and repeating it on every item only made feeds longer.
    /// </summary>
    public static XElement BuildEpisodeItem(Episode episode, string? channelImage, XNamespace itunes, bool channelExplicit = false)
    {
        var audioAsset = SelectAudioAsset(episode);

        var imageUrl = PodcastHelpers.GetImageUrlFromAssets(episode.ImageAssets);
        if (imageUrl == channelImage)
            imageUrl = null;

        var rawTime = episode.StartTime ?? episode.PublishTime;
        var pubDate = TryParseFeedDate(rawTime, out var dt)
            ? FormatRfc822(dt)
            : rawTime ?? "";

        var item = new XElement("item",
            new XElement("title", episode.Title ?? ""),
            new XElement("description", episode.Description ?? ""),
            new XElement("pubDate", pubDate),
            episode.ExplicitContent == channelExplicit ? null : new XElement(itunes + "explicit", episode.ExplicitContent ? "yes" : "no"));

        // Whole seconds, as Apple recommends. An hh:mm:ss TimeSpan format wraps at 24 hours.
        if (episode.DurationMilliseconds is { } durationMs)
        {
            item.Add(new XElement(itunes + "duration", durationMs / 1000));
        }

        if (!string.IsNullOrEmpty(imageUrl))
        {
            item.Add(new XElement(itunes + "image", new XAttribute("href", FeedArtwork(imageUrl))));
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
            item.Add(new XElement("guid", new XAttribute("isPermaLink", "false"), episode.Id));
        }

        if (audioAsset?.Url is { } url && !string.IsNullOrEmpty(url))
        {
            // RSS 2.0 requires length; 0 is the conventional value when the size is unknown.
            item.Add(new XElement("enclosure",
                new XAttribute("url", url),
                new XAttribute("length", audioAsset.FileSize ?? 0),
                new XAttribute("type", "audio/mpeg")));
        }

        // No categories per episode: apps go by the channel's, and repeating them on every item only made
        // the feed longer.
        return item;
    }
}

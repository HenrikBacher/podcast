using System.Text.Encodings.Web;

namespace DrPodcast;

public static class WebsiteGenerator
{
    // `site/index.html` is the template, not a static asset. Copying it into the served
    // directory would publish the raw `{{FEED_COUNT}}` placeholders and an empty feed list
    // until GenerateIndexHtmlAsync overwrites it a moment later.
    private const string IndexFileName = "index.html";

    // The series descriptions, which only the expanded card shows; script.js fetches them on the first open.
    private const string DescriptionsFileName = "descriptions.json";

    // Covers loaded straight away: two rows of three on a wide screen, the first few on a phone.
    private const int EagerCovers = 6;

    private static readonly string[] DanishMonths = ["jan.", "feb.", "mar.", "apr.", "maj", "jun.", "jul.", "aug.", "sep.", "okt.", "nov.", "dec."];

    public static async Task GenerateAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        // Newest episode first, so the page opens on what's new. The browser re-sorts A–Å with Danish
        // collation, which the server can't do: it runs with InvariantGlobalization.
        var sortedFeeds = feeds
            .OrderByDescending(f => f.LatestEpisodeUtc ?? DateTime.MinValue)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            CopyStaticAssets(config, logger, cancellationToken);

            await GenerateIndexHtmlAsync(sortedFeeds, config, logger, cancellationToken);

            logger?.LogInformation("Website regenerated ({Count} feeds listed).", sortedFeeds.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Website generation failed");
            throw;
        }
    }

    private static void CopyStaticAssets(GeneratorConfig config, ILogger? logger, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(config.SiteSourceDir))
        {
            logger?.LogWarning("Site source directory '{Dir}' not found. Skipping static assets.", config.SiteSourceDir);
            return;
        }

        var sourceFull = Path.GetFullPath(config.SiteSourceDir);
        var copied = 0;
        var skipped = 0;
        foreach (var file in Directory.EnumerateFiles(sourceFull, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourceFull, file);
            if (string.Equals(relative, IndexFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(config.FullSiteDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // Skip files whose size and last-write-time match the source — preserves
            // Last-Modified for podcatcher caching and avoids unnecessary disk writes.
            var src = new FileInfo(file);
            var dst = new FileInfo(destination);
            if (dst.Exists && dst.Length == src.Length && dst.LastWriteTimeUtc == src.LastWriteTimeUtc)
            {
                skipped++;
                continue;
            }

            File.Copy(file, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, src.LastWriteTimeUtc);
            copied++;
            logger?.LogDebug("Copied {File} to site directory", relative);
        }
        logger?.LogDebug("Static assets: {Copied} copied, {Skipped} unchanged", copied, skipped);
    }

    private static async Task GenerateIndexHtmlAsync(List<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger, CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(config.SiteSourceDir, IndexFileName);
        if (!File.Exists(templatePath))
        {
            logger?.LogWarning("Template file '{Path}' not found. Skipping index.html generation.", templatePath);
            return;
        }

        var feedsHtml = GenerateFeedsHtml(feeds, config.BaseUrl);
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        // Own the output directory rather than relying on a static asset having been copied
        // there first — with no assets besides the template there'd be nothing to create it.
        Directory.CreateDirectory(config.FullSiteDir);

        // Written before the page that links to it. Its URL carries a hash of its content, so the hour it is
        // cached for can't pair a new page with descriptions that lack its podcasts.
        var descriptions = DescriptionsJson(feeds);
        await WriteIfChangedAsync(Path.Combine(config.FullSiteDir, DescriptionsFileName), descriptions, cancellationToken);

        var html = template
            .Replace("{{ASSET_VERSION}}", AssetVersion(config.SiteSourceDir))
            .Replace("{{DESCRIPTIONS_VERSION}}", Convert.ToHexStringLower(SHA256.HashData(descriptions))[..10])
            .Replace("{{DEPLOYMENT_TIME}}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
            .Replace("{{FEED_COUNT}}", feeds.Count.ToString(CultureInfo.InvariantCulture))
            .Replace("<!-- BEGIN_FEEDS -->", feedsHtml)
            .Replace("<!-- END_FEEDS -->", "");

        await WriteAtomicallyAsync(Path.Combine(config.FullSiteDir, IndexFileName), Encoding.UTF8.GetBytes(html), cancellationToken);
        logger?.LogDebug("Generated index.html with {Count} feeds", feeds.Count);
    }

    // Temp file then rename, so a crash mid-write can't leave a corrupt file being served.
    private static async Task WriteAtomicallyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    // Static-file ETags derive from mtime and length, so rewriting identical content would make every
    // browser holding it download it again once its cache expires.
    private static async Task WriteIfChangedAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && (await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(content))
            return;
        await WriteAtomicallyAsync(path, content, cancellationToken);
    }

    /// <summary>
    /// The descriptions by slug, which the expanded card fetches the first time one is opened. Embedded in
    /// every card they were about half of the compressed page, for a dialog most visitors never open. Sorted
    /// by slug, so the file (and the URL its hash goes into) only changes when a description does.
    /// </summary>
    internal static byte[] DescriptionsJson(IEnumerable<FeedMetadata> feeds)
    {
        using var buffer = new MemoryStream();
        // Relaxed escaping keeps æøå as two UTF-8 bytes instead of a six-byte \u escape. The file is parsed
        // as JSON, never embedded in HTML, so leaving < > & unescaped is safe.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var feed in feeds.Where(f => !string.IsNullOrEmpty(f.Description)).OrderBy(f => f.Slug, StringComparer.Ordinal))
                writer.WriteString(feed.Slug, feed.Description);
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// A short hash of the stylesheet and script, appended to their URLs. They're cached for an hour, so
    /// without it a returning visitor could get new markup with the previous release's CSS and JS.
    /// </summary>
    internal static string AssetVersion(string siteSourceDir)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in new[] { "styles.css", "script.js" })
        {
            var path = Path.Combine(siteSourceDir, name);
            if (File.Exists(path))
                hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..10];
    }

    /// <summary>
    /// The cover as the website shows it. DR's originals are 3000×3000 (~1.2 MB) and the cards show them at
    /// 96 px, so the page asks DR's image server for its 320 px version (~20–30 KB, WebP where the browser
    /// accepts it); that is its smallest size and still sharp on high-density screens. Feeds keep the
    /// full-size artwork podcast apps want.
    /// </summary>
    internal static string CardCover(string imageUrl) => PodcastHelpers.WithImageWidth(imageUrl, 320);

    internal static string FormatDanishDate(DateTime utc)
    {
        var local = ToDanishTime(utc);
        return $"{local.Day}. {DanishMonths[local.Month - 1]} {local.Year}";
    }

    /// <summary>
    /// Danish local time by the EU rule: UTC+2 from 01:00 UTC on the last Sunday of March to 01:00 UTC on
    /// the last Sunday of October, otherwise UTC+1. Computed rather than looked up, because the Alpine
    /// container ships no time-zone database. Episodes released just after midnight would otherwise show
    /// the previous day.
    /// </summary>
    internal static DateTime ToDanishTime(DateTime utc)
    {
        static DateTime LastSundayAt0100(int year, int month)
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month), 1, 0, 0, DateTimeKind.Utc);
            return last.AddDays(-(int)last.DayOfWeek);
        }
        var summer = utc >= LastSundayAt0100(utc.Year, 3) && utc < LastSundayAt0100(utc.Year, 10);
        return DateTime.SpecifyKind(utc.AddHours(summer ? 2 : 1), DateTimeKind.Unspecified);
    }

    private static string GenerateFeedsHtml(IEnumerable<FeedMetadata> feeds, string baseUrl)
    {
        // XElement escapes content and attributes. Without JavaScript a card is its podcast and a link to
        // its feed; script.js adds search, filtering, sorting, relative dates and the expanded card with
        // the copy button and app links, which it builds from the feed link.
        var feedElements = feeds.Select((feed, index) =>
        {
            var feedUrl = RssBuilder.FeedUrl(baseUrl, feed.Slug);
            var categories = feed.Categories?.Split('|') ?? [];

            // alt is empty: the title is right next to the cover, and screen readers would say it twice.
            var cover = string.IsNullOrEmpty(feed.ImageUrl)
                ? new XElement("div", new XAttribute("class", "cover"))
                : new XElement("img",
                    new XAttribute("class", "cover"),
                    new XAttribute("src", CardCover(feed.ImageUrl)),
                    new XAttribute("alt", ""),
                    new XAttribute("width", "96"),
                    new XAttribute("height", "96"),
                    // The first rows are on screen at once; lazy-loading them would only hold them back.
                    index < EagerCovers ? null : new XAttribute("loading", "lazy"),
                    index == 0 ? new XAttribute("fetchpriority", "high") : null);

            var latest = new XElement("p", new XAttribute("class", "latest"));
            if (!string.IsNullOrEmpty(feed.LatestEpisodeTitle))
                latest.Add("Seneste: ", new XElement("span", new XAttribute("class", "episode"), feed.LatestEpisodeTitle), " ");
            var when = new XElement("span", new XAttribute("class", "when"));
            if (feed.LatestEpisodeUtc is { } utc)
                when.Add("· ", new XElement("time",
                    new XAttribute("datetime", utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                    FormatDanishDate(utc)));
            if (feed.EpisodeCount is { } count)
                when.Add($" · {count} afsnit");
            latest.Add(when);

            return new XElement("li",
                new XAttribute("class", "card"),
                // Looks the card's description up in descriptions.json.
                new XAttribute("data-slug", feed.Slug),
                new XAttribute("data-title", feed.Title),
                new XAttribute("data-categories", feed.Categories ?? ""),
                // script.js marks the card "Ny podcast" while this is under 30 days old.
                feed.FirstEpisodeUtc is { } first
                    ? new XAttribute("data-first-episode", first.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                    : null,
                cover,
                new XElement("div", new XAttribute("class", "info"),
                    new XElement("div", new XAttribute("class", "tags"),
                        categories.Select(c => new XElement("span", new XAttribute("class", "tag"), c))),
                    new XElement("h2", feed.Title),
                    string.IsNullOrEmpty(feed.Subtitle) ? null : new XElement("p", new XAttribute("class", "sub"), feed.Subtitle),
                    latest,
                    new XElement("a", new XAttribute("class", "url"), new XAttribute("href", feedUrl), feedUrl)));
        });

        return string.Join("\n", feedElements.Select(
            e => "        " + e.ToString(SaveOptions.DisableFormatting)));
    }
}

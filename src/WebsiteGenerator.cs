namespace DrPodcast;

public static class WebsiteGenerator
{
    public static async Task GenerateAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var sortedFeeds = feeds.OrderBy(f => f.Title).ToList();

        try
        {
            logger?.LogInformation("Generating website...");

            CopyStaticAssets(config, logger, cancellationToken);

            await GenerateIndexHtmlAsync(sortedFeeds, config, logger, cancellationToken);

            logger?.LogInformation("Website generation complete!");
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
        logger?.LogInformation("Static assets: {Copied} copied, {Skipped} unchanged", copied, skipped);
    }

    private static async Task GenerateIndexHtmlAsync(List<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger, CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(config.SiteSourceDir, "index.html");
        if (!File.Exists(templatePath))
        {
            logger?.LogWarning("Template file '{Path}' not found. Skipping index.html generation.", templatePath);
            return;
        }

        var feedsHtml = GenerateFeedsHtml(feeds);
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        var html = template
            .Replace("{{DEPLOYMENT_TIME}}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            .Replace("{{FEED_COUNT}}", feeds.Count.ToString())
            .Replace("<!-- BEGIN_FEEDS -->", feedsHtml)
            .Replace("<!-- END_FEEDS -->", "");

        // Atomic write: temp file then rename so a crash mid-write can't leave a corrupt index served.
        var outputPath = Path.Combine(config.FullSiteDir, "index.html");
        var tempPath = outputPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, html, cancellationToken);
        File.Move(tempPath, outputPath, overwrite: true);
        logger?.LogInformation("Generated index.html with {Count} feeds", feeds.Count);
    }

    private static string GenerateFeedsHtml(IEnumerable<FeedMetadata> feeds)
    {
        var feedElements = feeds.Select(feed =>
        {
            // XElement handles proper escaping automatically for both content and attributes
            var imageElement = string.IsNullOrEmpty(feed.ImageUrl)
                ? new XElement("div", new XAttribute("class", "feed-icon"))
                : new XElement("img",
                    new XAttribute("class", "feed-icon"),
                    new XAttribute("src", feed.ImageUrl),
                    new XAttribute("loading", "lazy"),
                    new XAttribute("alt", feed.Title));

            return new XElement("li",
                new XElement("a",
                    new XAttribute("class", "feed-link"),
                    new XAttribute("href", $"feeds/{feed.Slug}.xml"),
                    imageElement,
                    new XElement("span",
                        new XAttribute("class", "feed-title"),
                        feed.Title
                    )
                )
            );
        });

        return string.Join("\n", feedElements.Select(
            e => "        " + e.ToString(SaveOptions.DisableFormatting)));
    }
}

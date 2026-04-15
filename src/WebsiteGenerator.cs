namespace DrPodcast;

public static class WebsiteGenerator
{
    private static readonly ConcurrentDictionary<string, (DateTime Mtime, long Size, string Hash)> HashCache = new();

    public static async Task GenerateAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig? config = null, ILogger? logger = null)
    {
        config ??= new GeneratorConfig();
        var sortedFeeds = feeds.OrderBy(f => f.Title).ToList();

        try
        {
            logger?.LogInformation("Generating website...");

            // Create output directories
            Directory.CreateDirectory(config.FullSiteDir);
            Directory.CreateDirectory(config.FeedsDir);

            // Copy static assets
            CopyStaticAssets(config, logger);

            // Generate index.html with feed list
            await GenerateIndexHtmlAsync(sortedFeeds, config, logger);

            // Generate manifest.json
            await GenerateManifestAsync(sortedFeeds, config, logger);

            logger?.LogInformation("Website generation complete!");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Website generation failed");
            throw;
        }
    }

    private static void CopyStaticAssets(GeneratorConfig config, ILogger? logger)
    {
        if (!Directory.Exists(config.SiteSourceDir))
        {
            logger?.LogWarning("Site source directory '{Dir}' not found. Skipping static assets.", config.SiteSourceDir);
            return;
        }

        foreach (var file in Directory.GetFiles(config.SiteSourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(config.FullSiteDir, fileName), overwrite: true);
            logger?.LogDebug("Copied {File} to site directory", fileName);
        }
    }

    private static async Task GenerateIndexHtmlAsync(List<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger)
    {
        var templatePath = Path.Combine(config.SiteSourceDir, "index.html");
        if (!File.Exists(templatePath))
        {
            logger?.LogWarning("Template file '{Path}' not found. Skipping index.html generation.", templatePath);
            return;
        }

        var feedsHtml = GenerateFeedsHtml(feeds);
        var template = await File.ReadAllTextAsync(templatePath);

        // Replace template placeholders
        var html = template
            .Replace("{{DEPLOYMENT_TIME}}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            .Replace("{{FEED_COUNT}}", feeds.Count.ToString())
            .Replace("<!-- BEGIN_FEEDS -->", feedsHtml)
            .Replace("<!-- END_FEEDS -->", "");

        var outputPath = Path.Combine(config.FullSiteDir, "index.html");
        await File.WriteAllTextAsync(outputPath, html);
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

    private static async Task GenerateManifestAsync(List<FeedMetadata> feeds, GeneratorConfig config, ILogger? logger)
    {
        var feedEntries = await Task.WhenAll(feeds.Select(async feed =>
        {
            var feedPath = Path.Combine(config.FeedsDir, $"{feed.Slug}.xml");

            if (!File.Exists(feedPath))
            {
                logger?.LogWarning("Feed file not found for manifest: {Path}", feedPath);
                return (FeedFileInfo?)null;
            }

            var fileInfo = new FileInfo(feedPath);
            var hash = await GetOrComputeHashAsync(feedPath, fileInfo);

            return new FeedFileInfo(
                Name: $"{feed.Slug}.xml",
                Hash: hash,
                Size: fileInfo.Length,
                Title: feed.Title
            );
        }));

        var feedFiles = feedEntries.OfType<FeedFileInfo>().ToList();

        var manifest = new FeedManifest(
            Timestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            FeedCount: feedFiles.Count,
            Feeds: feedFiles
        );

        var manifestPath = Path.Combine(config.FullSiteDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, PodcastJsonContext.Default.FeedManifest);
        await File.WriteAllTextAsync(manifestPath, json);

        logger?.LogInformation("Generated manifest.json with {Count} feeds", feedFiles.Count);
    }

    private static async Task<string> GetOrComputeHashAsync(string filePath, FileInfo fileInfo)
    {
        var mtime = fileInfo.LastWriteTimeUtc;
        var size = fileInfo.Length;

        if (HashCache.TryGetValue(filePath, out var cached) && cached.Mtime == mtime && cached.Size == size)
            return cached.Hash;

        var hash = await ComputeFileHashAsync(filePath);
        HashCache[filePath] = (mtime, size, hash);
        return hash;
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

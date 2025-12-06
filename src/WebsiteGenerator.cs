namespace DrPodcast;

public static class WebsiteGenerator
{
    public static async Task GenerateAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig? config = null)
    {
        config ??= new GeneratorConfig();

        try
        {
            Console.WriteLine("Generating website...");

            // Create output directories
            Directory.CreateDirectory(config.FullSiteDir);
            Directory.CreateDirectory(config.FeedsDir);

            // Copy static assets
            await CopyStaticAssetsAsync(config);

            // Generate index.html with feed list
            await GenerateIndexHtmlAsync(feeds, config);

            // Generate manifest.json
            await GenerateManifestAsync(feeds, config);

            Console.WriteLine("Website generation complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Website generation failed: {ex.Message}");
            throw;
        }
    }

    private static async Task CopyStaticAssetsAsync(GeneratorConfig config)
    {
        if (!Directory.Exists(config.SiteSourceDir))
        {
            Console.WriteLine($"Warning: Site source directory '{config.SiteSourceDir}' not found. Skipping static assets.");
            return;
        }

        var files = Directory.GetFiles(config.SiteSourceDir);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(config.FullSiteDir, fileName);

            // Use async file copy
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            await using var dest = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await source.CopyToAsync(dest);
            Console.WriteLine($"Copied {fileName} to site directory");
        }
    }

    private static async Task GenerateIndexHtmlAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig config)
    {
        var templatePath = Path.Combine(config.SiteSourceDir, "index.html");
        if (!File.Exists(templatePath))
        {
            Console.WriteLine($"Warning: Template file '{templatePath}' not found. Skipping index.html generation.");
            return;
        }

        var feedsList = feeds.OrderBy(f => f.Title).ToList();
        var feedsHtml = GenerateFeedsHtml(feedsList);
        var template = await File.ReadAllTextAsync(templatePath);

        // Replace template placeholders
        var html = template
            .Replace("{{DEPLOYMENT_TIME}}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            .Replace("{{FEED_COUNT}}", feedsList.Count.ToString())
            .Replace("<!-- BEGIN_FEEDS -->", feedsHtml)
            .Replace("<!-- END_FEEDS -->", "");

        var outputPath = Path.Combine(config.FullSiteDir, "index.html");
        await File.WriteAllTextAsync(outputPath, html);
        Console.WriteLine($"Generated index.html with {feedsList.Count} feeds");
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

        var feedsContainer = new XElement("root", feedElements);

        // Extract inner content (without root wrapper)
        var sb = new StringBuilder();
        foreach (var element in feedsContainer.Elements())
        {
            sb.AppendLine("        " + element.ToString(SaveOptions.DisableFormatting));
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task GenerateManifestAsync(IEnumerable<FeedMetadata> feeds, GeneratorConfig config)
    {
        var feedFiles = new List<FeedFileInfo>();

        foreach (var feed in feeds)
        {
            var feedPath = Path.Combine(config.FeedsDir, $"{feed.Slug}.xml");

            if (!File.Exists(feedPath))
            {
                Console.WriteLine($"Warning: Feed file not found for manifest: {feedPath}");
                continue;
            }

            var fileInfo = new FileInfo(feedPath);
            var hash = await ComputeFileHashAsync(feedPath);

            feedFiles.Add(new FeedFileInfo(
                Name: $"{feed.Slug}.xml",
                Hash: hash,
                Size: fileInfo.Length,
                Title: feed.Title
            ));
        }

        var manifest = new FeedManifest(
            Timestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            FeedCount: feedFiles.Count,
            Feeds: feedFiles.OrderBy(f => f.Title).ToList()
        );

        var manifestPath = Path.Combine(config.FullSiteDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, PodcastJsonContext.Default.FeedManifest);
        await File.WriteAllTextAsync(manifestPath, json);

        Console.WriteLine($"Generated manifest.json with {feedFiles.Count} feeds");
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream);
        // Use Convert.ToHexString instead of BitConverter.ToString for better performance
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

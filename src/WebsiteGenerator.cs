namespace DrPodcast;

public static class WebsiteGenerator
{
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


}

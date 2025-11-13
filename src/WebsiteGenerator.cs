namespace DrPodcast;

public static class WebsiteGenerator
{
    public static void Generate(IEnumerable<FeedMetadata> feeds, GeneratorConfig? config = null)
    {
        config ??= new GeneratorConfig();

        try
        {
            Console.WriteLine("Generating website...");

            // Create output directories
            Directory.CreateDirectory(config.FullSiteDir);
            Directory.CreateDirectory(config.FeedsDir);

            // Copy static assets
            CopyStaticAssets(config);

            // Generate index.html with feed list
            GenerateIndexHtml(feeds, config);

            Console.WriteLine("Website generation complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Website generation failed: {ex.Message}");
            throw;
        }
    }

    private static void CopyStaticAssets(GeneratorConfig config)
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
            File.Copy(file, destFile, overwrite: true);
            Console.WriteLine($"Copied {fileName} to site directory");
        }
    }

    private static void GenerateIndexHtml(IEnumerable<FeedMetadata> feeds, GeneratorConfig config)
    {
        var templatePath = Path.Combine(config.SiteSourceDir, "index.html");
        if (!File.Exists(templatePath))
        {
            Console.WriteLine($"Warning: Template file '{templatePath}' not found. Skipping index.html generation.");
            return;
        }

        var feedsList = feeds.OrderBy(f => f.Title).ToList();
        var feedsHtml = GenerateFeedsHtml(feedsList);
        var template = File.ReadAllText(templatePath);

        // Replace template placeholders
        var html = template
            .Replace("{{DEPLOYMENT_TIME}}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
            .Replace("{{FEED_COUNT}}", feedsList.Count.ToString())
            .Replace("<!-- BEGIN_FEEDS -->", feedsHtml)
            .Replace("<!-- END_FEEDS -->", "");

        var outputPath = Path.Combine(config.FullSiteDir, "index.html");
        File.WriteAllText(outputPath, html);
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
}

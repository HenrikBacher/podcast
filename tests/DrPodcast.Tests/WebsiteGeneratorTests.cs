namespace DrPodcast.Tests;

public class WebsiteGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "drpodcast-web-" + Guid.NewGuid().ToString("N"));

    private GeneratorConfig CreateConfig() => new(
        OutputDir: Path.Combine(_root, "output"),
        SiteDir: "_site",
        SiteSourceDir: Path.Combine(_root, "site"),
        BaseUrl: "https://example.com");

    private const string Template = """
        <!DOCTYPE html>
        <html lang="da">
        <head><meta name="deployment-time" content="{{DEPLOYMENT_TIME}}"></head>
        <body>
            <p><span class="feed-count">{{FEED_COUNT}}</span> feeds</p>
            <ul class="feeds">
                <!-- BEGIN_FEEDS -->
                <!-- END_FEEDS -->
            </ul>
        </body>
        </html>
        """;

    private void WriteSiteSource(GeneratorConfig config, params (string Name, string Content)[] extraAssets)
    {
        Directory.CreateDirectory(config.SiteSourceDir);
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "index.html"), Template);
        foreach (var (name, content) in extraAssets)
            File.WriteAllText(Path.Combine(config.SiteSourceDir, name), content);
    }

    private static string IndexPath(GeneratorConfig config) => Path.Combine(config.FullSiteDir, "index.html");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // The template lives alongside the static assets. Copying it verbatim into the served
    // directory would publish raw {{FEED_COUNT}} placeholders and an empty feed list.
    [Fact]
    public async Task Generate_DoesNotPublishRawTemplatePlaceholders()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("slug-a", "Podcast A", null)], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().NotContain("{{FEED_COUNT}}");
        html.Should().NotContain("{{DEPLOYMENT_TIME}}");
        html.Should().NotContain("<!-- BEGIN_FEEDS -->");
        html.Should().Contain("feeds/slug-a.xml");
    }

    [Fact]
    public async Task Generate_RunTwice_StillLeavesRenderedIndexOnDisk()
    {
        var config = CreateConfig();
        WriteSiteSource(config);
        List<FeedMetadata> feeds = [new("slug-a", "Podcast A", null)];

        await WebsiteGenerator.GenerateAsync(feeds, config);
        await WebsiteGenerator.GenerateAsync(feeds, config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().NotContain("{{FEED_COUNT}}");
        html.Should().Contain("feeds/slug-a.xml");
    }

    [Fact]
    public async Task Generate_CopiesNonTemplateAssets()
    {
        var config = CreateConfig();
        WriteSiteSource(config, ("styles.css", "body{}"), ("script.js", "// hi"));

        await WebsiteGenerator.GenerateAsync([], config);

        File.Exists(Path.Combine(config.FullSiteDir, "styles.css")).Should().BeTrue();
        File.Exists(Path.Combine(config.FullSiteDir, "script.js")).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_SubstitutesFeedCount()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
            [new FeedMetadata("a", "A", null), new FeedMetadata("b", "B", null)], config);

        (await File.ReadAllTextAsync(IndexPath(config)))
            .Should().Contain(">2</span> feeds");
    }

    [Fact]
    public async Task Generate_SortsFeedsCaseInsensitively()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
            [new FeedMetadata("zebra", "zebra", null), new FeedMetadata("apple", "Apple", null)], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.IndexOf("feeds/apple.xml", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("feeds/zebra.xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_EscapesTitlesAndImageUrls()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
            [new FeedMetadata("x", "Rock & \"Roll\" <b>", "https://asset.dr.dk/i.jpg?a=1&b=2")], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().NotContain("<b>");
        html.Should().Contain("&amp;");
    }

    [Fact]
    public async Task Generate_MissingSiteSourceDir_DoesNotThrow()
    {
        var config = CreateConfig();

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null)], config);

        File.Exists(IndexPath(config)).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_NoTemplate_LeavesPreviousIndexIntact()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.SiteSourceDir);
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "styles.css"), "body{}");
        Directory.CreateDirectory(config.FullSiteDir);
        await File.WriteAllTextAsync(IndexPath(config), "previous");

        await WebsiteGenerator.GenerateAsync([], config);

        (await File.ReadAllTextAsync(IndexPath(config))).Should().Be("previous");
    }
}

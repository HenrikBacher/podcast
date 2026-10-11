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

    [Fact]
    public async Task Generate_LinksEveryCardToTheUnchangedFeedUrl()
    {
        // Existing subscriptions point at {BASE_URL}/feeds/{slug}.xml; the page must hand out exactly that.
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("slug-a", "Podcast A", null)], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().Contain("data-url=\"https://example.com/feeds/slug-a.xml\"")
            .And.Contain("href=\"https://example.com/feeds/slug-a.xml\"")
            .And.Contain("href=\"https://antennapod.org/deeplink/subscribe?url=https%3A%2F%2Fexample.com%2Ffeeds%2Fslug-a.xml&amp;title=Podcast%20A\"")
            .And.Contain("href=\"pktc://subscribe/example.com/feeds/slug-a.xml\"");
    }

    [Fact]
    public async Task Generate_ListsTheNewestEpisodeFirst()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
        [
            new FeedMetadata("older", "Aaa", null, LatestEpisodeUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FeedMetadata("newer", "Zzz", null, LatestEpisodeUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
        ], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.IndexOf("feeds/newer.xml", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("feeds/older.xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generate_RendersEpisodeDetailsAndCategories()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
            [new FeedMetadata("a", "A", "https://asset.dr.dk/i", "En undertitel", "Dokumentar|Historie", 128, "Sidste afsnit",
                new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc))], config);

        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().Contain("En undertitel")
            .And.Contain("Seneste: <span class=\"episode\">Sidste afsnit</span>")
            .And.Contain("<time datetime=\"2026-10-08T03:00:00Z\">8. okt. 2026</time>")
            .And.Contain("128 afsnit")
            .And.Contain("data-categories=\"Dokumentar|Historie\"")
            .And.Contain("alt=\"\"");
    }

    [Theory]
    [InlineData("2026-10-04T22:30:00Z", "5. okt. 2026")]  // 00:30 Danish summer time
    [InlineData("2026-01-10T23:30:00Z", "11. jan. 2026")] // 00:30 Danish winter time
    [InlineData("2026-03-29T00:30:00Z", "29. mar. 2026")] // just before the switch to summer time
    [InlineData("2026-10-25T00:30:00Z", "25. okt. 2026")] // last hour of summer time
    [InlineData("2026-12-31T22:59:00Z", "31. dec. 2026")]
    public void FormatDanishDate_UsesDanishLocalTime(string utc, string expected)
    {
        WebsiteGenerator.FormatDanishDate(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal))
            .Should().Be(expected);
    }

    [Fact]
    public async Task Generate_VersionsTheStylesheetAndScriptByContent()
    {
        var config = CreateConfig();
        Directory.CreateDirectory(config.SiteSourceDir);
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "index.html"), "<link href=\"styles.css?v={{ASSET_VERSION}}\"><!-- BEGIN_FEEDS --><!-- END_FEEDS -->");
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "styles.css"), "body{}");

        await WebsiteGenerator.GenerateAsync([], config);
        var first = await File.ReadAllTextAsync(IndexPath(config));
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "styles.css"), "body{color:red}");
        await WebsiteGenerator.GenerateAsync([], config);
        var second = await File.ReadAllTextAsync(IndexPath(config));

        first.Should().MatchRegex("styles\\.css\\?v=[0-9a-f]{10}\"").And.NotBe(second);
    }

    [Fact]
    public void AppLinks_BaseUrlWithoutScheme_KeepsTheWholeHost()
    {
        WebsiteGenerator.AppLinks("podcasts.example.dk/feeds/x.xml", "X")
            .Should().Contain(("Pocket Casts", "pktc://subscribe/podcasts.example.dk/feeds/x.xml"));
    }

    [Fact]
    public async Task Generate_EmbedsTheFullDescriptionHiddenForTheDialog()
    {
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
            [new FeedMetadata("a", "A", null, Description: "Første afsnit.\nVært: <Lasse> & co.")], config);

        (await File.ReadAllTextAsync(IndexPath(config)))
            .Should().Contain("<p class=\"description\" hidden=\"\">Første afsnit.\nVært: &lt;Lasse&gt; &amp; co.</p>");
    }
}

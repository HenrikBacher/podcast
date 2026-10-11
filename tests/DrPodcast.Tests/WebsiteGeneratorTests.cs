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
        // script.js builds the copy button and app links of the expanded card from this link.
        html.Should().Contain("<a class=\"url\" href=\"https://example.com/feeds/slug-a.xml\">https://example.com/feeds/slug-a.xml</a>");
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
    public async Task Generate_LoadsTheFirstCoversStraightAway()
    {
        // The first rows are on screen at once; the rest load as they're scrolled to.
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(Enumerable.Range(0, 8).Select(i => new FeedMetadata($"p{i}", $"P{i}", $"https://asset.dr.dk/i{i}.jpg")), config);

        var covers = System.Text.RegularExpressions.Regex.Matches(await File.ReadAllTextAsync(IndexPath(config)), "<img [^>]*>").Select(m => m.Value).ToList();
        covers.Should().HaveCount(8);
        covers[0].Should().Contain("fetchpriority=\"high\"").And.NotContain("loading=");
        covers[1..6].Should().AllSatisfy(c => c.Should().NotContain("loading=").And.NotContain("fetchpriority"));
        covers[6..].Should().AllSatisfy(c => c.Should().Contain("loading=\"lazy\""));
    }

    [Fact]
    public async Task Generate_PutsDescriptionsInTheirOwnFileBySlug()
    {
        // Only the expanded card shows them; embedded in every card they were half the compressed page.
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync(
        [
            new FeedMetadata("b", "B", null, Description: "Første afsnit.\nVært: <Lasse> & co."),
            new FeedMetadata("a", "A", null, Description: "Om A"),
            new FeedMetadata("c", "C", null),
        ], config);

        (await File.ReadAllTextAsync(Path.Combine(config.FullSiteDir, "descriptions.json")))
            .Should().Be("""{"a":"Om A","b":"Første afsnit.\nVært: <Lasse> & co."}""");
        var html = await File.ReadAllTextAsync(IndexPath(config));
        html.Should().Contain("data-slug=\"b\"").And.NotContain("Første afsnit");
    }

    [Fact]
    public async Task Generate_LinksDescriptionsByContentHash()
    {
        // Cached for an hour, so a new page must not reuse the URL of descriptions that lack its podcasts.
        var config = CreateConfig();
        WriteSiteSource(config);
        File.WriteAllText(Path.Combine(config.SiteSourceDir, "index.html"), "<ul data-descriptions=\"descriptions.json?v={{DESCRIPTIONS_VERSION}}\"></ul>");

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null, Description: "Om A")], config);
        var first = await File.ReadAllTextAsync(IndexPath(config));
        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null, Description: "Om A")], config);
        var same = await File.ReadAllTextAsync(IndexPath(config));
        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null, Description: "Nyt om A")], config);
        var changed = await File.ReadAllTextAsync(IndexPath(config));

        first.Should().MatchRegex("descriptions\\.json\\?v=[0-9a-f]{10}\"").And.Be(same).And.NotBe(changed);
    }

    [Fact]
    public async Task Generate_LeavesUnchangedDescriptionsUntouched()
    {
        // Its ETag derives from the mtime, so a rewrite would make browsers download it again.
        var config = CreateConfig();
        WriteSiteSource(config);
        var path = Path.Combine(config.FullSiteDir, "descriptions.json");
        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null, Description: "Om A")], config);
        var earlier = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(path, earlier);

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", null, Description: "Om A")], config);

        File.GetLastWriteTimeUtc(path).Should().Be(earlier);
    }

    [Fact]
    public async Task Generate_ShowsDrsSmallCoverOnCards()
    {
        // The 3000×3000 originals are ~1.2 MB each; the card shows 96 px.
        var config = CreateConfig();
        WriteSiteSource(config);

        await WebsiteGenerator.GenerateAsync([new FeedMetadata("a", "A", "https://asset.dr.dk/drlyd/images/urn:dr:radio:image:abc")], config);

        (await File.ReadAllTextAsync(IndexPath(config)))
            .Should().Contain("src=\"https://asset.dr.dk/drlyd/images/urn:dr:radio:image:abc?imwidth=320\"");
    }

    [Fact]
    public void CardCover_AppendsToAnExistingQuery()
    {
        WebsiteGenerator.CardCover("https://asset.dr.dk/i.jpg?a=1").Should().Be("https://asset.dr.dk/i.jpg?a=1&imwidth=320");
    }
}

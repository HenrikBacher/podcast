namespace DrPodcast.Tests;

public class PodcastCatalogTests
{
    private static Series DrSeries(string id, string slug, string? productionNumber) =>
        TestData.CreateSeries() with { Id = id, Slug = slug, ProductionNumber = productionNumber };

    [Theory]
    [InlineData("genstart-2642056922000", "2642056922000", "genstart")]
    [InlineData("sara-og-monopolet-podcast-1090358387000", "1090358387000", "sara-og-monopolet-podcast")]
    [InlineData("udsyn-759561186000", "759561186000", "udsyn")]
    [InlineData("no-suffix", "123456789012", "no-suffix")]       // suffix missing: slug kept as is
    [InlineData("genstart-2642056922000", null, "genstart-2642056922000")]
    public void SlugFor_DropsTheProductionNumberSuffix(string drSlug, string? productionNumber, string expected)
    {
        PodcastCatalog.SlugFor(DrSeries("urn:a", drSlug, productionNumber)).Should().Be(expected);
    }

    [Theory]
    [InlineData("../etc-123")]
    [InlineData("Genstart-123")]
    [InlineData("-123")]
    public void SlugFor_RejectsAnythingThatIsntASafeFileName(string drSlug)
    {
        PodcastCatalog.SlugFor(DrSeries("urn:a", drSlug, "123")).Should().BeNull();
    }

    [Fact]
    public void Build_SameDerivedSlug_FirstByIdKeepsItTheOtherGetsItsProductionNumber()
    {
        var catalog = PodcastCatalog.Build([DrSeries("urn:b", "show-222", "222"), DrSeries("urn:a", "show-111", "111")]);

        catalog.Podcasts.Select(p => (p.Urn, p.Slug)).Should().Equal(("urn:a", "show"), ("urn:b", "show-222"));
    }

    [Fact]
    public void Build_CarriesTheListedSeriesAlong()
    {
        var series = DrSeries("urn:a", "show-111", "111");

        PodcastCatalog.Build([series]).Podcasts.Single().Series.Should().Be(series);
    }
}

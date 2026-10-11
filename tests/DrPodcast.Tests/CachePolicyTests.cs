using Microsoft.AspNetCore.Http;

namespace DrPodcast.Tests;

public class CachePolicyTests
{
    private const string Version = "0123456789";

    private static string For(string path, string contentType, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return CachePolicy.For(context.Request, contentType, Version).ToString();
    }

    [Fact]
    public void Feeds_AreRevalidatedEveryTime() =>
        For("/feeds/a.xml", "application/xml; charset=utf-8").Should().Be("public, no-cache");

    [Fact]
    public void IndexHtml_IsRevalidatedEveryTime() =>
        For("/index.html", "text/html; charset=utf-8").Should().Be("public, no-cache");

    [Theory]
    [InlineData("/styles.css", "text/css; charset=utf-8")]
    [InlineData("/script.js", "application/javascript; charset=utf-8")]
    public void AssetsAtTheCurrentHash_AreImmutable(string path, string contentType) =>
        For(path, contentType, "?v=" + Version).Should().Be("public, max-age=31536000, immutable");

    [Theory]
    [InlineData("")]
    [InlineData("?v=fedcba9876")]
    public void AssetsWithoutTheCurrentHash_AreCachedForAnHour(string query) =>
        For("/styles.css", "text/css; charset=utf-8", query).Should().Be("public, max-age=3600");

    [Fact]
    public void Fonts_AreImmutable() =>
        For("/fonts/instrument-sans-400-600.woff2", "font/woff2").Should().Be("public, max-age=31536000, immutable");

    [Fact]
    public void AVersionQueryOnAFeed_DoesNotMakeItImmutable() =>
        For("/feeds/a.xml", "application/xml; charset=utf-8", "?v=" + Version).Should().Be("public, no-cache");
}

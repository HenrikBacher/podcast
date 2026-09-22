namespace DrPodcast.Tests;

public class DrApiClientTests
{
    [Theory]
    [InlineData("https://api.dr.dk/radio/v2/series/urn:dr:radio:series:abc/episodes?limit=256&offset=256")]
    [InlineData("https://API.DR.DK/radio/v2/series/x/episodes?offset=512")]
    public void ResolveNextUrl_DrApiHttps_IsFollowed(string next)
    {
        DrApiClient.ResolveNextUrl(next).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/radio/v2/series/x/episodes?offset=256")]
    [InlineData("http://api.dr.dk/radio/v2/series/x/episodes")]
    [InlineData("https://evil.example/radio/v2/series/x/episodes")]
    [InlineData("https://api.dr.dk.evil.example/radio/v2/series/x/episodes")]
    [InlineData("https://user:pass@api.dr.dk/radio/v2/series/x/episodes")]
    [InlineData("https://api.dr.dk:8443/radio/v2/series/x/episodes")]
    [InlineData("file:///etc/passwd")]
    public void ResolveNextUrl_UntrustedOrMissing_ReturnsNull(string? next)
    {
        DrApiClient.ResolveNextUrl(next).Should().BeNull();
    }
}

namespace DrPodcast.Tests;

public class FeedRefreshBackgroundServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BackoffMinutes_AtOrBelowThreshold_UsesPlainInterval(int failures)
    {
        FeedRefreshBackgroundService.BackoffMinutes(15, failures).Should().Be(15);
    }

    [Theory]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    public void BackoffMinutes_DoublesPerExtraFailure(int failures, int expected)
    {
        FeedRefreshBackgroundService.BackoffMinutes(15, failures).Should().Be(expected);
    }

    [Fact]
    public void BackoffMinutes_IsCappedAtOneHour()
    {
        FeedRefreshBackgroundService.BackoffMinutes(15, 50).Should().Be(60);
    }

    // Regression: the old `intervalMinutes * (1 << shift)` with a 20-bit shift overflowed to a
    // negative delay for large intervals, which threw out of ExecuteAsync and stopped the host.
    [Theory]
    [InlineData(3000)]
    [InlineData(1440)]
    [InlineData(int.MaxValue)]
    public void BackoffMinutes_LargeInterval_StaysPositiveAndCapped(int intervalMinutes)
    {
        for (var failures = 0; failures <= 40; failures++)
        {
            var backoff = FeedRefreshBackgroundService.BackoffMinutes(intervalMinutes, failures);
            backoff.Should().BePositive();
            backoff.Should().BeLessThanOrEqualTo(60);
        }
    }

    [Fact]
    public void BackoffMinutes_NeverExceedsTaskDelayRange()
    {
        var backoff = FeedRefreshBackgroundService.BackoffMinutes(int.MaxValue, int.MaxValue);
        TimeSpan.FromMinutes(backoff).Should().BeGreaterThan(TimeSpan.Zero);
    }
}

public class GeneratorConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ParseRefreshInterval_InvalidValues_FallBackToDefault(string? raw)
    {
        GeneratorConfig.ParseRefreshInterval(raw)
            .Should().Be(GeneratorConfig.DefaultRefreshIntervalMinutes);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("15", 15)]
    [InlineData("60", 60)]
    public void ParseRefreshInterval_ValidValues_AreUsed(string raw, int expected)
    {
        GeneratorConfig.ParseRefreshInterval(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("100000")]
    [InlineData("2147483647")]
    public void ParseRefreshInterval_IsClampedToOneDay(string raw)
    {
        GeneratorConfig.ParseRefreshInterval(raw).Should().Be(24 * 60);
    }

    [Fact]
    public void ReadinessStaleAfter_TracksRefreshInterval()
    {
        new GeneratorConfig(RefreshIntervalMinutes: 60).ReadinessStaleAfter
            .Should().Be(TimeSpan.FromMinutes(240));
    }

    [Fact]
    public void ReadinessStaleAfter_HasOneHourFloorForShortIntervals()
    {
        new GeneratorConfig(RefreshIntervalMinutes: 1).ReadinessStaleAfter
            .Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void FeedsDir_IsNestedUnderSiteDir()
    {
        var config = new GeneratorConfig(OutputDir: "out", SiteDir: "_site");
        config.FeedsDir.Should().Be(Path.Combine("out", "_site", "feeds"));
    }
}

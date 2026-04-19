using DrPodcast;
using FluentAssertions;

namespace DrPodcast.Tests;

public class FeedGenerationServiceTests
{
    #region HasNewerEpisodes

    [Fact]
    public void HasNewerEpisodes_NullLatestEpisodeStartTime_ReturnsTrue()
    {
        var series = CreateSeries(latestEpisodeStartTime: null);
        FeedGenerationService.HasNewerEpisodes("nonexistent.xml", series).Should().BeTrue();
    }

    [Fact]
    public void HasNewerEpisodes_FeedWithOlderDate_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>Mon, 01 Jan 2024 10:00:00 +0000</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-06-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_FeedWithSameDate_ReturnsFalse()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write a feed with a lastBuildDate matching the series timestamp
            var dt = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
            var rfc822 = RssBuilder.FormatRfc822(dt);
            File.WriteAllText(tempFile, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>{rfc822}</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00+00:00");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_CorruptFile_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid xml");

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_MissingLastBuildDate_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <title>Test</title>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_NonexistentFile_ReturnsTrue()
    {
        var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
        FeedGenerationService.HasNewerEpisodes("does-not-exist.xml", series).Should().BeTrue();
    }

    [Fact]
    public void HasNewerEpisodes_OldColonTimezoneFormat_IsParsed()
    {
        // Feeds generated before the +0000 switch contain "+00:00" — the parser
        // must continue to accept them or every existing feed regenerates on startup.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>Mon, 15 Jan 2024 10:00:00 +00:00</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00+00:00");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_MalformedLastBuildDate_ReturnsTrue()
    {
        // If the element is present but the date is unparseable, we must regenerate
        // rather than trusting a value we can't compare against.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>not a date at all</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_FeedNewerThanApi_ReturnsFalse()
    {
        // Defensive case: if the feed on disk is strictly newer than the API's
        // latest timestamp (clock skew, reverted episode), don't rewrite.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>Sat, 15 Jun 2024 10:00:00 +0000</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void HasNewerEpisodes_DtdDeclaration_IsRejected()
    {
        // Feed parsing must refuse DTDs to avoid XXE / billion-laughs in case a
        // malicious file lands in the feeds directory.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE rss [<!ENTITY x "y">]>
                <rss version="2.0">
                  <channel>
                    <lastBuildDate>Mon, 15 Jan 2024 10:00:00 +0000</lastBuildDate>
                  </channel>
                </rss>
                """);

            var series = CreateSeries(latestEpisodeStartTime: "2024-01-15T10:00:00Z");
            FeedGenerationService.HasNewerEpisodes(tempFile, series).Should().BeTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Helpers

    private static Series CreateSeries(
        string? presentationType = null,
        int numberOfSeries = 0,
        string? defaultOrder = null,
        string? latestEpisodeStartTime = null,
        string? title = "Test Series")
    {
        return new Series(
            Type: "podcast",
            Categories: null,
            NumberOfEpisodes: 0,
            NumberOfSeries: numberOfSeries,
            NumberOfSeasons: 0,
            PresentationType: presentationType,
            GroupingType: null,
            LatestEpisodeStartTime: latestEpisodeStartTime,
            PresentationUrl: null,
            ExplicitContent: false,
            DefaultOrder: defaultOrder,
            Id: "test-id",
            Slug: "test-slug",
            Title: title,
            Punchline: null,
            Description: "Test description",
            ImageAssets: null
        );
    }

    #endregion
}

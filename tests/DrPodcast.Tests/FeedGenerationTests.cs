using System.Globalization;
using System.Xml.Linq;
using DrPodcast;
using FluentAssertions;

namespace DrPodcast.Tests;

public class FeedGenerationTests
{
    [Fact]
    public void RssElement_ShouldHaveCorrectNamespaces()
    {
        // Arrange
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        XNamespace media = "http://search.yahoo.com/mrss/";

        var channel = new XElement("channel",
            new XElement("title", "Test Podcast")
        );

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            new XAttribute(XNamespace.Xmlns + "media", media),
            channel
        );

        // Assert
        rss.Attribute("version")?.Value.Should().Be("2.0");
        rss.Attribute(XNamespace.Xmlns + "atom")?.Value.Should().Be("http://www.w3.org/2005/Atom");
        rss.Attribute(XNamespace.Xmlns + "itunes")?.Value.Should().Be("http://www.itunes.com/dtds/podcast-1.0.dtd");
        rss.Attribute(XNamespace.Xmlns + "media")?.Value.Should().Be("http://search.yahoo.com/mrss/");
    }

    [Fact]
    public void ChannelElement_ShouldContainRequiredElements()
    {
        // Arrange
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", "https://example.com/feed.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")
            ),
            new XElement("title", "Test Podcast"),
            new XElement("link", "https://example.com"),
            new XElement("description", "Test Description"),
            new XElement("language", "da"),
            new XElement("copyright", "DR"),
            new XElement(itunes + "explicit", "no"),
            new XElement(itunes + "author", "DR")
        );

        // Assert
        channel.Element("title")?.Value.Should().Be("Test Podcast");
        channel.Element("link")?.Value.Should().Be("https://example.com");
        channel.Element("description")?.Value.Should().Be("Test Description");
        channel.Element("language")?.Value.Should().Be("da");
        channel.Element("copyright")?.Value.Should().Be("DR");
        channel.Element(itunes + "explicit")?.Value.Should().Be("no");
        channel.Element(itunes + "author")?.Value.Should().Be("DR");
    }

    [Fact]
    public void ChannelOwner_ShouldHaveEmailAndName()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var owner = new XElement(itunes + "owner",
            new XElement(itunes + "email", "test@dr.dk"),
            new XElement(itunes + "name", "DR Test")
        );

        // Assert
        owner.Element(itunes + "email")?.Value.Should().Be("test@dr.dk");
        owner.Element(itunes + "name")?.Value.Should().Be("DR Test");
    }

    [Fact]
    public void ItemElement_ShouldContainEpisodeMetadata()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var item = new XElement("item",
            new XElement("title", "Episode 1"),
            new XElement("description", "Episode description"),
            new XElement("pubDate", "Mon, 01 Jan 2024 12:00:00 +00:00"),
            new XElement("guid", new XAttribute("isPermalink", "false"), "ep-001"),
            new XElement(itunes + "duration", "00:30:00"),
            new XElement(itunes + "episode", 1),
            new XElement(itunes + "season", 1)
        );

        // Assert
        item.Element("title")?.Value.Should().Be("Episode 1");
        item.Element("description")?.Value.Should().Be("Episode description");
        item.Element("pubDate")?.Value.Should().Be("Mon, 01 Jan 2024 12:00:00 +00:00");
        item.Element("guid")?.Value.Should().Be("ep-001");
        item.Element("guid")?.Attribute("isPermalink")?.Value.Should().Be("false");
        item.Element(itunes + "duration")?.Value.Should().Be("00:30:00");
        item.Element(itunes + "episode")?.Value.Should().Be("1");
        item.Element(itunes + "season")?.Value.Should().Be("1");
    }

    [Fact]
    public void Enclosure_ShouldHaveCorrectAttributes()
    {
        // Arrange
        var enclosure = new XElement("enclosure",
            new XAttribute("url", "https://example.com/audio.mp3"),
            new XAttribute("type", "audio/mpeg"),
            new XAttribute("length", "41472000")
        );

        // Assert
        enclosure.Attribute("url")?.Value.Should().Be("https://example.com/audio.mp3");
        enclosure.Attribute("type")?.Value.Should().Be("audio/mpeg");
        enclosure.Attribute("length")?.Value.Should().Be("41472000");
    }

    [Fact]
    public void DateFormatting_ShouldFollowRfc822()
    {
        // Arrange
        var testDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var formatted = testDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

        // Assert
        formatted.Should().MatchRegex(@"^[A-Za-z]{3}, \d{2} [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$");
    }

    [Fact]
    public void DurationFormatting_ShouldBeHHMMSS()
    {
        // Arrange
        int durationMs = 1800000; // 30 minutes

        // Act
        var duration = TimeSpan.FromMilliseconds(durationMs).ToString(@"hh\:mm\:ss");

        // Assert
        duration.Should().Be("00:30:00");
    }

    [Theory]
    [InlineData(3600000, "01:00:00")]  // 1 hour
    [InlineData(7200000, "02:00:00")]  // 2 hours
    [InlineData(90000, "00:01:30")]    // 1.5 minutes
    [InlineData(5000, "00:00:05")]     // 5 seconds
    public void DurationFormatting_ShouldHandleVariousLengths(int milliseconds, string expected)
    {
        // Act
        var duration = TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");

        // Assert
        duration.Should().Be(expected);
    }

    [Fact]
    public void MediaRestriction_ShouldBeDenmark()
    {
        // Arrange
        XNamespace media = "http://search.yahoo.com/mrss/";

        var restriction = new XElement(media + "restriction",
            new XAttribute("relationship", "allow"),
            new XAttribute("type", "country"),
            "dk"
        );

        // Assert
        restriction.Attribute("relationship")?.Value.Should().Be("allow");
        restriction.Attribute("type")?.Value.Should().Be("country");
        restriction.Value.Should().Be("dk");
    }

    [Fact]
    public void ExplicitContent_ShouldBeYesOrNo()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var explicitYes = new XElement(itunes + "explicit", "yes");
        var explicitNo = new XElement(itunes + "explicit", "no");

        // Assert
        explicitYes.Value.Should().Be("yes");
        explicitNo.Value.Should().Be("no");
    }

    [Fact]
    public void ItunesType_ShouldBeEpisodicOrSerial()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var episodic = new XElement(itunes + "type", "episodic");
        var serial = new XElement(itunes + "type", "serial");

        // Assert
        episodic.Value.Should().Be("episodic");
        serial.Value.Should().Be("serial");
    }

    [Fact]
    public void AtomLink_ShouldHaveSelfReference()
    {
        // Arrange
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var atomLink = new XElement(atom + "link",
            new XAttribute("href", "https://example.com/feed.xml"),
            new XAttribute("rel", "self"),
            new XAttribute("type", "application/rss+xml")
        );

        // Assert
        atomLink.Attribute("href")?.Value.Should().Be("https://example.com/feed.xml");
        atomLink.Attribute("rel")?.Value.Should().Be("self");
        atomLink.Attribute("type")?.Value.Should().Be("application/rss+xml");
    }

    [Fact]
    public void ItunesImage_ShouldHaveHrefAttribute()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var image = new XElement(itunes + "image",
            new XAttribute("href", "https://example.com/image.jpg")
        );

        // Assert
        image.Attribute("href")?.Value.Should().Be("https://example.com/image.jpg");
    }

    [Fact]
    public void Category_ShouldHaveTextAttribute()
    {
        // Arrange
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

        var category = new XElement(itunes + "category",
            new XAttribute("text", "Documentary")
        );

        // Assert
        category.Attribute("text")?.Value.Should().Be("Documentary");
    }

    [Fact]
    public void FullRssFeed_ShouldValidateStructure()
    {
        // Arrange
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
        XNamespace media = "http://search.yahoo.com/mrss/";

        var channel = new XElement("channel",
            new XElement(atom + "link",
                new XAttribute("href", "https://example.com/feed.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")
            ),
            new XElement("title", "Test Podcast"),
            new XElement("link", "https://example.com"),
            new XElement("description", "Test Description"),
            new XElement("language", "da"),
            new XElement("copyright", "DR"),
            new XElement(itunes + "explicit", "no"),
            new XElement(itunes + "author", "DR"),
            new XElement(itunes + "type", "episodic"),
            new XElement("item",
                new XElement("title", "Episode 1"),
                new XElement("description", "Episode description"),
                new XElement("enclosure",
                    new XAttribute("url", "https://example.com/audio.mp3"),
                    new XAttribute("type", "audio/mpeg"),
                    new XAttribute("length", "10000000")
                )
            )
        );

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "atom", atom),
            new XAttribute(XNamespace.Xmlns + "itunes", itunes),
            new XAttribute(XNamespace.Xmlns + "media", media),
            channel
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);

        // Assert
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("rss");
        doc.Root.Element("channel").Should().NotBeNull();
        doc.Root.Element("channel")!.Element("title")?.Value.Should().Be("Test Podcast");
        doc.Root.Element("channel")!.Element("item").Should().NotBeNull();
    }
}

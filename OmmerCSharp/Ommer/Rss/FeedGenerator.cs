using System.Xml;
using Microsoft.Extensions.Logging;

namespace Ommer.Rss;

public static class FeedGenerator
{
    public static void Generate(this Feed feed, string feedFilePath, ILogger logger)
    {
        try
        {
            logger.LogInformation("Creating XML document for feed: {Title}", feed.Title);
            
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = System.Text.Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            Directory.CreateDirectory(Path.GetDirectoryName(feedFilePath) ?? throw new InvalidOperationException("Invalid file path"));

            using var writer = XmlWriter.Create(feedFilePath, settings);
            
            logger.LogDebug("Building RSS feed structure");
            
            writer.WriteStartDocument(true);
            writer.WriteStartElement("rss");
            writer.WriteAttributeString("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
            writer.WriteAttributeString("xmlns", "media", null, "http://search.yahoo.com/mrss/");
            writer.WriteAttributeString("xmlns", "itunes", null, "http://www.itunes.com/dtds/podcast-1.0.dtd");
            writer.WriteAttributeString("version", "2.0");

            writer.WriteStartElement("channel");

            // atom:link
            writer.WriteStartElement("atom", "link", "http://www.w3.org/2005/Atom");
            writer.WriteAttributeString("href", feed.FeedUrl);
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("type", "application/rss+xml");
            writer.WriteEndElement();

            writer.WriteElementString("title", feed.Title);
            writer.WriteElementString("link", feed.Link);
            writer.WriteElementString("description", feed.Description);
            writer.WriteElementString("language", feed.Language);
            writer.WriteElementString("copyright", feed.Copyright);
            writer.WriteElementString("managingEditor", feed.Email);
            writer.WriteElementString("lastBuildDate", feed.LastBuildDate);
            writer.WriteElementString("itunes", "explicit", "http://www.itunes.com/dtds/podcast-1.0.dtd", 
                feed.Explicit ? "yes" : "no");
            writer.WriteElementString("itunes", "author", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Author);
            writer.WriteElementString("itunes", "block", "http://www.itunes.com/dtds/podcast-1.0.dtd", "yes");

            // itunes:owner
            writer.WriteStartElement("itunes", "owner", "http://www.itunes.com/dtds/podcast-1.0.dtd");
            writer.WriteElementString("itunes", "email", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Email);
            writer.WriteElementString("itunes", "name", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.OwnerName);
            writer.WriteEndElement();

            writer.WriteElementString("itunes", "new-feed-url", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.FeedUrl);

            // itunes:image
            writer.WriteStartElement("itunes", "image", "http://www.itunes.com/dtds/podcast-1.0.dtd");
            writer.WriteAttributeString("href", feed.ImageUrl);
            writer.WriteEndElement();

            // image
            writer.WriteStartElement("image");
            writer.WriteElementString("url", feed.ImageUrl);
            writer.WriteElementString("title", feed.Title);
            writer.WriteElementString("link", feed.Link);
            writer.WriteEndElement();

            writer.WriteElementString("itunes", "category", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Category);

            // media:restriction
            writer.WriteStartElement("media", "restriction", "http://search.yahoo.com/mrss/");
            writer.WriteAttributeString("type", "country");
            writer.WriteAttributeString("relationship", "allow");
            writer.WriteString(feed.MediaRestrictionCountry);
            writer.WriteEndElement();

            // Items
            foreach (var item in feed.Items)
            {
                writer.WriteStartElement("item");

                // guid
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermalink", "false");
                writer.WriteString(item.Guid);
                writer.WriteEndElement();

                writer.WriteElementString("link", item.Link);
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("description", item.Description);
                writer.WriteElementString("pubDate", item.PubDate);
                writer.WriteElementString("explicit", item.Explicit ? "yes" : "no");
                writer.WriteElementString("itunes", "author", "http://www.itunes.com/dtds/podcast-1.0.dtd", item.Author);
                writer.WriteElementString("itunes", "duration", "http://www.itunes.com/dtds/podcast-1.0.dtd", item.Duration);

                // media:restriction
                writer.WriteStartElement("media", "restriction", "http://search.yahoo.com/mrss/");
                writer.WriteAttributeString("type", "country");
                writer.WriteAttributeString("relationship", "allow");
                writer.WriteString(item.MediaRestrictionCountry);
                writer.WriteEndElement();

                // enclosure
                writer.WriteStartElement("enclosure");
                writer.WriteAttributeString("url", item.EnclosureUrl);
                writer.WriteAttributeString("type", "audio/mpeg");
                writer.WriteAttributeString("length", item.EnclosureByteLength.ToString());
                writer.WriteEndElement();

                writer.WriteEndElement(); // item
            }

            writer.WriteEndElement(); // channel
            writer.WriteEndElement(); // rss
            writer.WriteEndDocument();

            logger.LogInformation("Successfully wrote feed file to: {FilePath}", Path.GetFullPath(feedFilePath));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to generate RSS feed");
            throw new InvalidOperationException($"Failed to generate RSS feed: {e.Message}", e);
        }
    }
}

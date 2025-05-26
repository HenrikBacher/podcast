using System.Xml;
using Microsoft.Extensions.Logging;
using   ;
using Microsoft.SyndicationFeed.Rss;

namespace Ommer.Rss;

public static class FeedGenerator
{
    public static async Task GenerateAsync(this Feed feed, string feedFilePath, ILogger logger)
    {
        try
        {
            logger.LogInformation("Creating RSS feed for: {Title}", feed.Title);
            
            Directory.CreateDirectory(Path.GetDirectoryName(feedFilePath) ?? throw new InvalidOperationException("Invalid file path"));

            var settings = new XmlWriterSettings
            {
                Async = true,
                Indent = true,
                IndentChars = "  ",
                Encoding = System.Text.Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using var fileStream = new FileStream(feedFilePath, FileMode.Create, FileAccess.Write);
            using var writer = XmlWriter.Create(fileStream, settings);
            
            logger.LogDebug("Building RSS feed structure using Microsoft.SyndicationFeed");

            var feedWriter = new RssFeedWriter(writer);

            // Write RSS/Channel opening with namespaces
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(null, "rss", null);
            await writer.WriteAttributeStringAsync(null, "version", null, "2.0");
            await writer.WriteAttributeStringAsync("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
            await writer.WriteAttributeStringAsync("xmlns", "media", null, "http://search.yahoo.com/mrss/");
            await writer.WriteAttributeStringAsync("xmlns", "itunes", null, "http://www.itunes.com/dtds/podcast-1.0.dtd");
            
            await writer.WriteStartElementAsync(null, "channel", null);

            // Use SyndicationFeed for standard RSS elements
            await feedWriter.WriteTitle(feed.Title);
            await feedWriter.WriteDescription(feed.Description);
            await feedWriter.WriteValue("link", feed.Link);
            await feedWriter.WriteLanguage(feed.Language);
            await feedWriter.WriteCopyright(feed.Copyright);
            await feedWriter.WriteManagingEditor(feed.Email);
            await feedWriter.WriteLastBuildDate(DateTimeOffset.Parse(feed.LastBuildDate));

            // Write custom elements that SyndicationFeed doesn't support well
            await writer.WriteStartElementAsync("atom", "link", "http://www.w3.org/2005/Atom");
            await writer.WriteAttributeStringAsync(null, "href", null, feed.FeedUrl);
            await writer.WriteAttributeStringAsync(null, "rel", null, "self");
            await writer.WriteAttributeStringAsync(null, "type", null, "application/rss+xml");
            await writer.WriteEndElementAsync();
            
            // iTunes elements
            await writer.WriteElementStringAsync("itunes", "explicit", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Explicit ? "yes" : "no");
            await writer.WriteElementStringAsync("itunes", "author", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Author);
            await writer.WriteElementStringAsync("itunes", "block", "http://www.itunes.com/dtds/podcast-1.0.dtd", "yes");
            await writer.WriteElementStringAsync("itunes", "new-feed-url", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.FeedUrl);

            // iTunes owner
            await writer.WriteStartElementAsync("itunes", "owner", "http://www.itunes.com/dtds/podcast-1.0.dtd");
            await writer.WriteElementStringAsync("itunes", "email", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.Email);
            await writer.WriteElementStringAsync("itunes", "name", "http://www.itunes.com/dtds/podcast-1.0.dtd", feed.OwnerName);
            await writer.WriteEndElementAsync();
            
            // iTunes image
            await writer.WriteStartElementAsync("itunes", "image", "http://www.itunes.com/dtds/podcast-1.0.dtd");
            await writer.WriteAttributeStringAsync(null, "href", null, feed.ImageUrl);
            await writer.WriteEndElementAsync();
            
            // RSS image
            await writer.WriteStartElementAsync(null, "image", null);
            await writer.WriteElementStringAsync(null, "url", null, feed.ImageUrl);
            await writer.WriteElementStringAsync(null, "title", null, feed.Title);
            await writer.WriteElementStringAsync(null, "link", null, feed.Link);
            await writer.WriteEndElementAsync();

            // iTunes category
            await writer.WriteStartElementAsync("itunes", "category", "http://www.itunes.com/dtds/podcast-1.0.dtd");
            await writer.WriteAttributeStringAsync(null, "text", null, feed.Category);
            await writer.WriteEndElementAsync();
            
            // Media restriction
            await writer.WriteStartElementAsync("media", "restriction", "http://search.yahoo.com/mrss/");
            await writer.WriteAttributeStringAsync(null, "type", null, "country");
            await writer.WriteAttributeStringAsync(null, "relationship", null, "allow");
            await writer.WriteStringAsync(feed.MediaRestrictionCountry);
            await writer.WriteEndElementAsync();

            // Write items using SyndicationItem
            logger.LogDebug("Writing {ItemCount} feed items", feed.Items.Count);
            
            foreach (var feedItem in feed.Items)
            {
                var item = new SyndicationItem
                {
                    Id = feedItem.Guid,
                    Title = feedItem.Title,
                    Description = feedItem.Description,
                    Published = DateTimeOffset.Parse(feedItem.PubDate)
                };

                item.AddLink(new SyndicationLink(new Uri(feedItem.Link)));
                item.AddLink(new SyndicationLink(new Uri(feedItem.EnclosureUrl), "enclosure")
                {
                    MediaType = "audio/mpeg",
                    Length = feedItem.EnclosureByteLength
                });

                await feedWriter.Write(item);

                // Write custom iTunes and media elements after each item
                // (This is a limitation of the library - we need to write custom elements manually)
                await writer.WriteElementStringAsync("itunes", "author", "http://www.itunes.com/dtds/podcast-1.0.dtd", feedItem.Author);
                await writer.WriteElementStringAsync("itunes", "duration", "http://www.itunes.com/dtds/podcast-1.0.dtd", feedItem.Duration);
                await writer.WriteElementStringAsync(null, "explicit", null, feedItem.Explicit ? "yes" : "no");
                
                await writer.WriteStartElementAsync("media", "restriction", "http://search.yahoo.com/mrss/");
                await writer.WriteAttributeStringAsync(null, "type", null, "country");
                await writer.WriteAttributeStringAsync(null, "relationship", null, "allow");
                await writer.WriteStringAsync(feedItem.MediaRestrictionCountry);
                await writer.WriteEndElementAsync();
            }

            await writer.WriteEndElementAsync(); // channel
            await writer.WriteEndElementAsync(); // rss
            await writer.WriteEndDocumentAsync();

            await writer.FlushAsync();

            logger.LogInformation("Successfully wrote RSS feed to: {FilePath}", Path.GetFullPath(feedFilePath));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to generate RSS feed");
            throw new InvalidOperationException($"Failed to generate RSS feed: {e.Message}", e);
        }
    }

    // Keep synchronous version for backward compatibility
    public static void Generate(this Feed feed, string feedFilePath, ILogger logger)
    {
        GenerateAsync(feed, feedFilePath, logger).GetAwaiter().GetResult();
    }
}

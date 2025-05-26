using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Ommer.Client;
using Ommer.DrApi;
using Ommer.Extensions;
using Ommer.Rss;

namespace Ommer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var logger = loggerFactory.CreateLogger<Program>();

        try
        {
            var rootCommand = new RootCommand("Ommer - DR Podcast Feed Generator");

            var slugOption = new Option<string>(
                name: "--slug",
                description: "Podcast slug")
            { IsRequired = true };

            var urnOption = new Option<string>(
                name: "--urn", 
                description: "Podcast URN, id format used by dr")
            { IsRequired = true };

            var imageUrlOption = new Option<string>(
                name: "--imageUrl",
                description: "Podcast image URL, found in the rss feed on dr.dk/lyd")
            { IsRequired = true };

            var apiKeyOption = new Option<string>(
                name: "--apiKey",
                description: "API key for dr api")
            { IsRequired = true };

            var baseUrlOption = new Option<string>(
                name: "--baseUrl",
                description: "Base URL for hosting")
            { IsRequired = true };

            rootCommand.AddOption(slugOption);
            rootCommand.AddOption(urnOption);
            rootCommand.AddOption(imageUrlOption);
            rootCommand.AddOption(apiKeyOption);
            rootCommand.AddOption(baseUrlOption);

            rootCommand.SetHandler(async (slug, urn, imageUrl, apiKey, baseUrl) =>
            {
                await ProcessPodcastAsync(slug, urn, imageUrl, apiKey, baseUrl, logger, loggerFactory);
            }, slugOption, urnOption, imageUrlOption, apiKeyOption, baseUrlOption);

            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Application failed");
            return 1;
        }
    }

    private static async Task ProcessPodcastAsync(
        string slug, 
        string urn, 
        string imageUrl, 
        string apiKey, 
        string baseUrl,
        ILogger<Program> logger,
        ILoggerFactory loggerFactory)
    {
        logger.LogInformation("Starting podcast feed generation for {Slug}", slug);
        
        const string apiUri = "https://api.dr.dk/radio/v2";
        var feedUrl = $"{baseUrl}feeds/{slug}.xml";
        var outputDirectory = "output";

        var podcast = new Podcast(
            Urn: urn,
            Slug: slug,
            TitleSuffix: "(Reproduceret feed)",
            DescriptionSuffix: "",
            FeedUrl: feedUrl,
            ImageUrl: imageUrl
        );

        var rssDateTimeFormat = "ddd, dd MMM yyyy HH:mm:ss zzz";
        var danishCulture = new CultureInfo("da-DK");

        Directory.CreateDirectory(outputDirectory);
        var feedFilePath = Path.Combine(outputDirectory, $"{podcast.Slug}.xml");
        logger.LogInformation("Processing podcast {Slug}. Target feed: {FeedFile}", podcast.Slug, feedFilePath);

        using var httpClient = new HttpClient();
        using var apiClient = new DrApiClient(httpClient, loggerFactory.CreateLogger<DrApiClient>());

        try
        {
            // Fetch show information
            var showInfo = await apiClient.FetchShowInfoAsync(apiUri, podcast.Urn, apiKey);
            logger.LogInformation("Generating feed for show: {Title}", showInfo.Title);

            // Fetch episodes
            var episodes = await apiClient.FetchEpisodesAsync($"{apiUri}/series", podcast.Urn, apiKey);

            // Create feed items
            var feedItems = new List<FeedItem>();
            foreach (var item in episodes)
            {
                var audioAsset = item.AudioAssets
                    .Where(a => a.Format == "mp3")
                    .OrderBy(a => Math.Abs(a.Bitrate - 192))
                    .FirstOrDefault();

                if (audioAsset == null)
                {
                    logger.LogWarning("No audio asset for {Id} ({Title})", item.Id, item.Title);
                    continue;
                }

                var publishTime = DateTimeOffset.Parse(item.PublishTime);
                var copenhagenTime = TimeZoneInfo.ConvertTime(publishTime, 
                    TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"));

                var duration = TimeSpan.FromMilliseconds(item.DurationMilliseconds);

                var feedItem = new FeedItem(
                    Guid: item.ProductionNumber,
                    Link: item.PresentationUrl,
                    Title: item.Title,
                    Description: item.Description,
                    PubDate: copenhagenTime.ToString(rssDateTimeFormat, danishCulture),
                    Explicit: false,
                    Author: "DR",
                    Duration: duration.FormatHMS(),
                    MediaRestrictionCountry: "dk",
                    EnclosureUrl: audioAsset.Url,
                    EnclosureByteLength: audioAsset.FileSize
                );

                feedItems.Add(feedItem);
            }

            // Parse latest episode start time
            var latestEpisodeTime = DateTimeOffset.Parse(showInfo.LatestEpisodeStartTime);
            var latestEpisodeCopenhagenTime = TimeZoneInfo.ConvertTime(latestEpisodeTime, 
                TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"));

            // Create feed
            var feed = new Feed(
                Link: showInfo.PresentationUrl,
                Title: $"{showInfo.Title}{(podcast.TitleSuffix != null ? $" {podcast.TitleSuffix}" : "")}",
                Description: $"{showInfo.Description}{(podcast.DescriptionSuffix != null ? $"\n{podcast.DescriptionSuffix}" : "")}",
                Language: "da",
                Copyright: "DR",
                Email: "podcast@dr.dk",
                LastBuildDate: latestEpisodeCopenhagenTime.ToString(rssDateTimeFormat, danishCulture),
                Explicit: false,
                Author: "DR",
                OwnerName: "DR",
                FeedUrl: podcast.FeedUrl ?? feedUrl,
                ImageUrl: podcast.ImageUrl ?? imageUrl,
                ImageLink: showInfo.PresentationUrl,
                Category: "News",
                MediaRestrictionCountry: "dk",
                Items: feedItems
            );

            // Generate XML feed
            feed.Generate(feedFilePath, logger);
            logger.LogInformation("Successfully generated feed at: {FeedPath}", Path.GetFullPath(feedFilePath));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to generate feed file");
            throw new InvalidOperationException($"Failed to generate feed file: {e.Message}", e);
        }
        finally
        {
            logger.LogInformation("Process completed");
        }
    }
}

namespace DrPodcast;

public sealed class FeedGenerationService(DrApiClient apiClient, ILogger<FeedGenerationService> logger)
{
    private const int MaxConcurrentPodcasts = 6;
    private static readonly TimeSpan PerPodcastTimeout = TimeSpan.FromMinutes(2);

    private long _lastSuccessfulRunUtcTicks;
    public DateTime? LastSuccessfulRunUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulRunUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public async Task GenerateFeedsAsync(string podcastsJsonPath, GeneratorConfig config, bool forceRegenerate = false, CancellationToken cancellationToken = default)
    {
        var podcastList = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(podcastsJsonPath, cancellationToken),
            PodcastJsonContext.Default.PodcastList);

        if (podcastList is null || podcastList.Podcasts.Count == 0)
        {
            logger.LogWarning("No podcasts found in {Path}", podcastsJsonPath);
            return;
        }

        Directory.CreateDirectory(config.FeedsDir);

        var results = new ConcurrentBag<ProcessResult>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentPodcasts,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(podcastList.Podcasts, parallelOptions, async (podcast, ct) =>
        {
            var result = await ProcessPodcastAsync(podcast, config, forceRegenerate, ct);
            if (result is { } r) results.Add(r);
        });

        var feedMetadata = results.Select(r => r.Metadata).ToList();
        var changedCount = results.Count(r => r.Changed);

        logger.LogInformation("Generated {Total} podcast feeds ({Changed} changed).", feedMetadata.Count, changedCount);

        if (changedCount > 0 || forceRegenerate)
        {
            await WebsiteGenerator.GenerateAsync(feedMetadata, config, logger);
        }
        else
        {
            logger.LogInformation("No feeds changed; skipped website regeneration.");
        }

        Interlocked.Exchange(ref _lastSuccessfulRunUtcTicks, DateTime.UtcNow.Ticks);
    }

    private readonly record struct ProcessResult(FeedMetadata Metadata, bool Changed);

    private async Task<ProcessResult?> ProcessPodcastAsync(Podcast podcast, GeneratorConfig config, bool forceRegenerate, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["Slug"] = podcast.Slug });

        // Per-podcast budget so one hung series can't starve the others up to the outer 10-minute cap.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerPodcastTimeout);
        var ct = timeoutCts.Token;

        try
        {
            var series = await apiClient.FetchSeriesAsync(podcast.Urn, ct);

            // Skip regeneration if the feed is already up-to-date, so Last-Modified stays stable.
            // On startup (forceRegenerate=true) this check is bypassed so code changes are applied.
            string outputPath = Path.Combine(config.FeedsDir, $"{podcast.Slug}.xml");
            if (!forceRegenerate && File.Exists(outputPath) && !HasNewerEpisodes(outputPath, series))
            {
                logger.LogInformation("Skipped (unchanged)");
                return new ProcessResult(RssBuilder.BuildFeedMetadata(podcast, series), Changed: false);
            }

            var episodes = await apiClient.FetchAllEpisodesAsync(podcast.Urn, ct);

            var (rss, metadata) = RssBuilder.BuildRssFeed(series, episodes, podcast, config.BaseUrl, config.PreferMp4);

            // Atomic write: write to temp file then rename to avoid serving partial files
            string tempPath = outputPath + ".tmp";
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var writer = new StreamWriter(fileStream, new UTF8Encoding(false)))
            {
                var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);
                xmlDoc.Save(writer);
            }
            File.Move(tempPath, outputPath, overwrite: true);

            logger.LogInformation("Generated");

            return new ProcessResult(metadata, Changed: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Timed out after {Timeout}s processing {Urn}", PerPodcastTimeout.TotalSeconds, podcast.Urn);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process {Urn}", podcast.Urn);
            return null;
        }
    }

    internal static bool HasNewerEpisodes(string feedPath, Series? series)
    {
        if (!DateTime.TryParse(series?.LatestEpisodeStartTime, out var latestEpisode))
            return true; // Can't determine, regenerate to be safe

        try
        {
            // Use XmlReader to stop as soon as <lastBuildDate> is found rather than
            // loading the entire feed (which can contain hundreds of episode elements).
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using var reader = XmlReader.Create(feedPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "lastBuildDate")
                    continue;

                var lastBuildDate = reader.ReadElementContentAsString();
                // Accept both +0000 (RFC 822) and +00:00 (old format) by
                // normalizing to the colon form that zzz expects.
                if (lastBuildDate.Length >= 5 && lastBuildDate[^5] is '+' or '-' && lastBuildDate[^3] != ':')
                    lastBuildDate = lastBuildDate.Insert(lastBuildDate.Length - 2, ":");
                if (!DateTime.TryParseExact(lastBuildDate, RssBuilder.Rfc822Format,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var existing))
                    return true;

                return latestEpisode > existing;
            }

            return true; // Element not found, regenerate to be safe
        }
        catch (Exception)
        {
            return true; // Corrupt/unreadable file, regenerate
        }
    }
}

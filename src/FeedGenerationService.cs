namespace DrPodcast;

public sealed class FeedGenerationService(DrApiClient apiClient, ILogger<FeedGenerationService> logger)
{
    private const int MaxConcurrentPodcasts = 6;
    private static readonly TimeSpan PerPodcastTimeout = TimeSpan.FromMinutes(2);

    // A run with too many failed podcasts must not flip readiness to "ok" — otherwise the
    // probe lies while the site serves stale or incomplete content. Require at least this
    // fraction of configured podcasts to succeed before recording a successful run.
    private const double MinSuccessFraction = 0.5;

    // Last-known metadata per slug, surviving across runs (this service is a singleton). Lets the
    // website listing keep showing a podcast whose fetch failed this tick — its previously generated
    // feed file is still on disk and served, so dropping it from index.html would be a lie.
    private readonly ConcurrentDictionary<string, FeedMetadata> _lastKnownMetadata = new();

    private long _lastSuccessfulRunUtcTicks;
    public DateTime? LastSuccessfulRunUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulRunUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public async Task GenerateFeedsAsync(PodcastList podcastList, GeneratorConfig config, bool forceRegenerate = false, CancellationToken cancellationToken = default)
    {
        if (podcastList.Podcasts.Count == 0)
        {
            logger.LogWarning("No podcasts to process.");
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

        foreach (var r in results)
            _lastKnownMetadata[r.Metadata.Slug] = r.Metadata;

        var changedSlugs = results.Where(r => r.Changed).Select(r => r.Metadata.Slug).Order(StringComparer.Ordinal).ToList();
        var changedCount = changedSlugs.Count;
        var configuredCount = podcastList.Podcasts.Count;
        var successCount = results.Count;

        // Build the listing from every configured podcast (using last-known metadata for any that
        // failed this run), not just this run's successes — otherwise a transient DR error removes
        // a still-served feed from index.html. Podcasts never successfully fetched have no file and
        // are correctly absent.
        var feedMetadata = podcastList.Podcasts
            .Select(p => _lastKnownMetadata.GetValueOrDefault(p.Slug))
            .OfType<FeedMetadata>()
            .ToList();

        if (changedCount > 0)
        {
            logger.LogInformation("Updated {Changed}/{Total} feeds: {Slugs}", changedCount, configuredCount, string.Join(", ", changedSlugs));
        }
        else
        {
            logger.LogInformation("No feed updates ({Success}/{Total} checked).", successCount, configuredCount);
        }

        if (successCount < configuredCount)
        {
            var successSlugs = results.Select(r => r.Metadata.Slug).ToHashSet(StringComparer.Ordinal);
            var failedSlugs = podcastList.Podcasts.Select(p => p.Slug).Where(s => !successSlugs.Contains(s));
            logger.LogWarning("{Failed} feeds failed to refresh: {Slugs}", configuredCount - successCount, string.Join(", ", failedSlugs));
        }

        // Record readiness before regenerating the website: the feeds are already written and
        // correct on disk at this point, and index.html is a cosmetic listing on top of them.
        // Letting a website failure suppress readiness would 503 the whole service over a page.
        var minRequired = (int)Math.Ceiling(configuredCount * MinSuccessFraction);
        var metThreshold = successCount >= minRequired;
        if (metThreshold)
        {
            Interlocked.Exchange(ref _lastSuccessfulRunUtcTicks, DateTime.UtcNow.Ticks);
        }

        if (changedCount > 0 || forceRegenerate)
        {
            try
            {
                await WebsiteGenerator.GenerateAsync(feedMetadata, config, logger, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Already logged by WebsiteGenerator. The feeds themselves are current, so the
                // run is not a failure — the next change will retry the listing.
                logger.LogWarning("Feeds are up to date but the website listing could not be regenerated.");
            }
        }

        if (!metThreshold)
        {
            logger.LogError("Run did not meet success threshold: {Success}/{Total} (required {Required}). Readiness will not advance.",
                successCount, configuredCount, minRequired);
            throw new FeedGenerationFailedException(successCount, configuredCount, minRequired);
        }
    }

    private readonly record struct ProcessResult(FeedMetadata Metadata, bool Changed);

    public sealed class FeedGenerationFailedException(int successCount, int configuredCount, int requiredCount)
        : Exception($"Only {successCount}/{configuredCount} podcasts succeeded; required at least {requiredCount}.")
    {
        public int SuccessCount { get; } = successCount;
        public int ConfiguredCount { get; } = configuredCount;
        public int RequiredCount { get; } = requiredCount;
    }

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

            // A 200 with a null body deserializes to null. Building a feed from it would emit an
            // empty <title>/<description>/image and overwrite a perfectly good file while reporting
            // success — treat it as a failed refresh and leave what's on disk alone.
            if (series is null)
            {
                logger.LogWarning("DR API returned no series body for {Urn}. Leaving the existing feed untouched.", podcast.Urn);
                return null;
            }

            // Skip regeneration if the feed is already up-to-date, so Last-Modified stays stable.
            // On startup (forceRegenerate=true) this check is bypassed so code changes are applied.
            string outputPath = Path.Combine(config.FeedsDir, $"{podcast.Slug}.xml");
            if (!forceRegenerate && File.Exists(outputPath) && !await HasNewerEpisodesAsync(outputPath, series, ct))
            {
                // The date check passed, but DR rotates audio asset hashes for already-published
                // episodes (re-encodes etc.) without bumping LatestEpisodeStartTime — leaving feeds
                // pointing at 404'd URLs. Verify the latest episode's current asset hash is in the
                // file before skipping.
                var latestEpisode = await apiClient.FetchLatestEpisodeAsync(podcast.Urn, ct);
                if (latestEpisode is null || await FeedReferencesLatestAssetAsync(outputPath, latestEpisode, ct))
                {
                    logger.LogDebug("Skipped (unchanged)");
                    return new ProcessResult(RssBuilder.BuildFeedMetadata(podcast, series), Changed: false);
                }
                logger.LogDebug("Regenerating: latest episode's audio asset hash has rotated");
            }

            var episodes = await apiClient.FetchAllEpisodesAsync(podcast.Urn, ct);

            // Never replace a populated feed with an empty one. An upstream hiccup that answers
            // with zero items would otherwise wipe the whole back catalogue and report success.
            // (A genuinely empty series with no file yet still gets its first, empty feed.)
            if (episodes is not { Count: > 0 } && File.Exists(outputPath))
            {
                logger.LogWarning("DR API returned no episodes for {Urn}. Keeping the existing feed.", podcast.Urn);
                return null;
            }

            var (rss, metadata) = RssBuilder.BuildRssFeed(series, episodes, podcast, config.BaseUrl);

            // Atomic write: write to temp file then rename to avoid serving partial files
            string tempPath = outputPath + ".tmp";
            try
            {
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    var xmlDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), rss);
                    var xmlSettings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false) };
                    await using var xmlWriter = XmlWriter.Create(fileStream, xmlSettings);
                    await xmlDoc.SaveAsync(xmlWriter, ct);
                }
                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch
            {
                // Don't leave a half-written .tmp behind for a failed or cancelled write.
                try { File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }

            logger.LogDebug("Generated");

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

    internal static async Task<bool> FeedReferencesLatestAssetAsync(string feedPath, Episode latestEpisode, CancellationToken cancellationToken = default)
    {
        var asset = RssBuilder.SelectAudioAsset(latestEpisode);
        if (asset?.Url is not { } url || string.IsNullOrEmpty(url))
            return true; // No audio to verify; nothing to regenerate against.

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;

        var match = RegexCache.DrAssetUrl().Match(uri.PathAndQuery);
        if (!match.Success)
            return true;

        var assetHash = match.Groups["asset"].Value;
        try
        {
            return await FileContainsAsciiAsync(feedPath, assetHash, cancellationToken);
        }
        catch (Exception)
        {
            return false; // Can't read — let the caller regenerate.
        }
    }

    // Kept under the 85 KB Large Object Heap threshold so the steady-state check never
    // touches the LOH.
    private const int SearchBufferSize = 64 * 1024;

    /// <summary>
    /// Streams <paramref name="feedPath"/> looking for an ASCII needle, without decoding the file.
    /// This runs for every unchanged podcast on every refresh tick, and the previous
    /// <c>File.ReadAllTextAsync</c> allocated roughly four times the feed size each time (raw bytes
    /// plus the UTF-16 decode) — all of it on the LOH for feeds over ~21 KB.
    /// </summary>
    internal static async Task<bool> FileContainsAsciiAsync(string feedPath, string ascii, CancellationToken cancellationToken)
    {
        if (ascii.Length == 0) return true;

        var needle = ArrayPool<byte>.Shared.Rent(ascii.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(SearchBufferSize);
        try
        {
            if (buffer.Length <= ascii.Length)
                return false; // Needle can't be matched against a buffer this small.

            Encoding.ASCII.GetBytes(ascii, needle.AsSpan(0, ascii.Length));
            var needleSpan = needle.AsMemory(0, ascii.Length);

            await using var stream = new FileStream(feedPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, SearchBufferSize, useAsync: true);

            // Bytes retained from the previous chunk so a needle straddling a chunk boundary
            // is still found.
            var carry = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(carry, buffer.Length - carry), cancellationToken);
                if (read == 0) return false;

                var available = carry + read;
                if (buffer.AsSpan(0, available).IndexOf(needleSpan.Span) >= 0)
                    return true;

                carry = Math.Min(ascii.Length - 1, available);
                buffer.AsSpan(available - carry, carry).CopyTo(buffer.AsSpan(0, carry));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(needle);
        }
    }

    internal static async Task<bool> HasNewerEpisodesAsync(string feedPath, Series? series, CancellationToken cancellationToken = default)
    {
        // Use DateTimeOffset throughout so timezone-naive API timestamps don't silently compare
        // wrong against feed timestamps that always carry an offset.
        if (!DateTimeOffset.TryParse(series?.LatestEpisodeStartTime, CultureInfo.InvariantCulture, RssBuilder.UtcParseStyles, out var latestEpisode))
            return true; // Can't determine, regenerate to be safe

        try
        {
            // Use XmlReader to stop as soon as <lastBuildDate> is found rather than
            // loading the entire feed (which can contain hundreds of episode elements).
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, Async = true };
            await using var fileStream = new FileStream(feedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            using var reader = XmlReader.Create(fileStream, settings);
            while (await reader.ReadAsync())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "lastBuildDate")
                    continue;

                var lastBuildDate = await reader.ReadElementContentAsStringAsync();
                // Accept both +0000 (RFC 822) and +00:00 (old format) by
                // normalizing to the colon form that zzz expects.
                if (lastBuildDate.Length >= 5 && lastBuildDate[^5] is '+' or '-' && lastBuildDate[^3] != ':')
                    lastBuildDate = lastBuildDate.Insert(lastBuildDate.Length - 2, ":");
                if (!DateTimeOffset.TryParseExact(lastBuildDate, RssBuilder.Rfc822Format,
                        CultureInfo.InvariantCulture, RssBuilder.UtcParseStyles, out var existing))
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

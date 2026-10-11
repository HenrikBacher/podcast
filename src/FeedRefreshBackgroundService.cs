namespace DrPodcast;

public sealed class FeedRefreshBackgroundService(
    FeedGenerationService feedService,
    GeneratorConfig config,
    ILogger<FeedRefreshBackgroundService> logger) : BackgroundService
{
    private const int MaxBackoffMinutes = 60;

    // Backoff starts after this many consecutive failures, then doubles per extra failure.
    private const int FailuresBeforeBackoff = 3;

    // MaxBackoffMinutes caps the result long before this, so a larger shift buys nothing and
    // only risks overflowing the multiplication.
    private const int MaxBackoffShift = 6;

    // Change detection only notices a newer episode, or a rotated audio hash on the latest one. A daily
    // full regeneration bounds how long anything else can stay stale: rotated hashes on older episodes,
    // edited titles and descriptions, removed episodes. Unchanged feeds come out byte-identical and
    // aren't rewritten, so this doesn't invalidate podcatchers' cached copies.
    private static readonly TimeSpan FullRefreshInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = config.RefreshIntervalMinutes;

        // Which podcasts exist is discovered from DR on every run, so new series appear by themselves.
        logger.LogInformation("Feed refresh service started. Interval: {Interval} minutes.", intervalMinutes);

        int consecutiveFailures = 0;
        // MinValue makes the first run, at startup, a full regeneration too: that's what applies code
        // changes when the container restarts.
        var lastFullRefreshUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var fullRefresh = DateTime.UtcNow - lastFullRefreshUtc >= FullRefreshInterval;
            if (fullRefresh)
                lastFullRefreshUtc = DateTime.UtcNow;
            consecutiveFailures = await RunGenerationAsync(config, consecutiveFailures, forceRegenerate: fullRefresh, stoppingToken);

            TimeSpan delay;
            if (consecutiveFailures >= FailuresBeforeBackoff)
            {
                var backoffMinutes = BackoffMinutes(intervalMinutes, consecutiveFailures);
                logger.LogWarning("Backing off for {Backoff} minutes after {Failures} consecutive failures.", backoffMinutes, consecutiveFailures);
                delay = TimeSpan.FromMinutes(backoffMinutes);
            }
            else
            {
                delay = TimeSpan.FromMinutes(intervalMinutes);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> RunGenerationAsync(GeneratorConfig config, int consecutiveFailures, bool forceRegenerate, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogDebug("Starting feed generation{Force}...", forceRegenerate ? " (forced)" : "");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
            await feedService.GenerateFeedsAsync(config, forceRegenerate, timeoutCts.Token);
            logger.LogDebug("Feed generation complete.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown
            return consecutiveFailures;
        }
        catch (OperationCanceledException)
        {
            consecutiveFailures++;
            logger.LogError("Feed generation timed out after 10 minutes ({Failures} consecutive failures).", consecutiveFailures);
            return consecutiveFailures;
        }
        catch (Exception ex)
        {
            consecutiveFailures++;
            logger.LogError(ex, "Feed generation failed ({Failures} consecutive). Will retry at next interval.", consecutiveFailures);
            return consecutiveFailures;
        }
    }

    /// <summary>
    /// Exponential backoff, capped at <see cref="MaxBackoffMinutes"/>. Computed in 64-bit: at the
    /// old 20-bit shift a large configured interval overflowed to a negative delay, which threw
    /// out of ExecuteAsync and — with the default StopHost behaviour — took the whole host down.
    /// </summary>
    internal static int BackoffMinutes(int intervalMinutes, int consecutiveFailures)
    {
        var shift = Math.Clamp(consecutiveFailures - FailuresBeforeBackoff, 0, MaxBackoffShift);
        return (int)Math.Min((long)intervalMinutes << shift, MaxBackoffMinutes);
    }
}

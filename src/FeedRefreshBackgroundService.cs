namespace DrPodcast;

public sealed class FeedRefreshBackgroundService(
    FeedGenerationService feedService,
    GeneratorConfig config,
    ILogger<FeedRefreshBackgroundService> logger) : BackgroundService
{
    private const int MaxBackoffMinutes = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var podcastsJsonPath = FindPodcastsJson();

        var intervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("REFRESH_INTERVAL_MINUTES"), out var mins) && mins > 0
            ? mins
            : 15;

        logger.LogInformation("Feed refresh service started. Interval: {Interval} minutes.", intervalMinutes);

        int consecutiveFailures = 0;

        // Force regeneration on startup so code changes are always applied when the container restarts
        consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, config, consecutiveFailures, forceRegenerate: true, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (consecutiveFailures >= 3)
            {
                var shift = Math.Min(consecutiveFailures - 3, 20);
                var backoffMinutes = Math.Min(intervalMinutes * (1 << shift), MaxBackoffMinutes);
                logger.LogWarning("Backing off for {Backoff} minutes after {Failures} consecutive failures.", backoffMinutes, consecutiveFailures);
                await Task.Delay(TimeSpan.FromMinutes(backoffMinutes), stoppingToken);
            }

            consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, config, consecutiveFailures, forceRegenerate: false, stoppingToken);
        }
    }

    private async Task<int> RunGenerationAsync(string podcastsJsonPath, GeneratorConfig config, int consecutiveFailures, bool forceRegenerate, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting feed generation{Force}...", forceRegenerate ? " (forced)" : "");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
            await feedService.GenerateFeedsAsync(podcastsJsonPath, config, forceRegenerate, timeoutCts.Token);
            logger.LogInformation("Feed generation complete.");
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

    private static string FindPodcastsJson() =>
        File.Exists("/app/podcasts.json") ? "/app/podcasts.json" : "podcasts.json";
}

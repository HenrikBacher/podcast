namespace DrPodcast;

public sealed class FeedRefreshBackgroundService(
    FeedGenerationService feedService,
    ILogger<FeedRefreshBackgroundService> logger) : BackgroundService
{
    private const int MaxBackoffMinutes = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
        var config = GeneratorConfig.FromEnvironment();
        var podcastsJsonPath = FindPodcastsJson();

        var intervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("REFRESH_INTERVAL_MINUTES"), out var mins) && mins > 0
            ? mins
            : 15;

        logger.LogInformation("Feed refresh service started. Interval: {Interval} minutes.", intervalMinutes);

        int consecutiveFailures = 0;

        // Force regeneration on startup so code changes are always applied when the container restarts
        consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, baseUrl, config, consecutiveFailures, forceRegenerate: true, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (consecutiveFailures >= 3)
            {
                var backoffMinutes = Math.Min(intervalMinutes * (1 << (consecutiveFailures - 2)), MaxBackoffMinutes);
                logger.LogWarning("Backing off for {Backoff} minutes after {Failures} consecutive failures.", backoffMinutes, consecutiveFailures);
                await Task.Delay(TimeSpan.FromMinutes(backoffMinutes), stoppingToken);
            }

            consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, baseUrl, config, consecutiveFailures, forceRegenerate: false, stoppingToken);
        }
    }

    private async Task<int> RunGenerationAsync(string podcastsJsonPath, string baseUrl, GeneratorConfig config, int consecutiveFailures, bool forceRegenerate, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting feed generation{Force}...", forceRegenerate ? " (forced)" : "");
            await feedService.GenerateFeedsAsync(podcastsJsonPath, baseUrl, config, forceRegenerate, cancellationToken);
            logger.LogInformation("Feed generation complete.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown
            return consecutiveFailures;
        }
        catch (Exception ex)
        {
            consecutiveFailures++;
            logger.LogError(ex, "Feed generation failed ({Failures} consecutive). Will retry at next interval.", consecutiveFailures);
            return consecutiveFailures;
        }
    }

    private static string FindPodcastsJson()
    {
        // Check common locations
        if (File.Exists("podcasts.json")) return "podcasts.json";
        if (File.Exists("/app/podcasts.json")) return "/app/podcasts.json";
        return "podcasts.json";
    }
}

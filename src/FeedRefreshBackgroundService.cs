namespace DrPodcast;

public sealed class FeedRefreshBackgroundService(
    FeedGenerationService feedService,
    FeedHealthStatus healthStatus,
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

        // Generate feeds immediately on startup
        consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, baseUrl, config, consecutiveFailures, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (consecutiveFailures >= 3)
            {
                var backoffMinutes = Math.Min(intervalMinutes * (1 << (consecutiveFailures - 2)), MaxBackoffMinutes);
                logger.LogWarning("Backing off for {Backoff} minutes after {Failures} consecutive failures.", backoffMinutes, consecutiveFailures);
                await Task.Delay(TimeSpan.FromMinutes(backoffMinutes), stoppingToken);
            }

            consecutiveFailures = await RunGenerationAsync(podcastsJsonPath, baseUrl, config, consecutiveFailures, stoppingToken);
        }
    }

    private async Task<int> RunGenerationAsync(string podcastsJsonPath, string baseUrl, GeneratorConfig config, int consecutiveFailures, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting feed generation...");
            var feedCount = await feedService.GenerateFeedsAsync(podcastsJsonPath, baseUrl, config, cancellationToken);
            healthStatus.ReportSuccess(feedCount);
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
            healthStatus.ReportFailure();
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

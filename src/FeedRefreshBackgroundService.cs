namespace DrPodcast;

public sealed class FeedRefreshBackgroundService(
    FeedGenerationService feedService,
    ILogger<FeedRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
        var config = GeneratorConfig.FromEnvironment();
        var podcastsJsonPath = FindPodcastsJson();

        var intervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("REFRESH_INTERVAL_MINUTES"), out var mins) && mins > 0
            ? mins
            : 15;

        logger.LogInformation("Feed refresh service started. Interval: {Interval} minutes.", intervalMinutes);

        // Generate feeds immediately on startup
        await RunGenerationAsync(podcastsJsonPath, baseUrl, config, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunGenerationAsync(podcastsJsonPath, baseUrl, config, stoppingToken);
        }
    }

    private async Task RunGenerationAsync(string podcastsJsonPath, string baseUrl, GeneratorConfig config, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting feed generation...");
            await feedService.GenerateFeedsAsync(podcastsJsonPath, baseUrl, config, cancellationToken);
            logger.LogInformation("Feed generation complete.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feed generation failed. Will retry at next interval.");
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

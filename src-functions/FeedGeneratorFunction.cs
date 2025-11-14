using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using DrPodcast;

namespace DrPodcast.Functions;

public class FeedGeneratorFunction
{
    private readonly ILogger<FeedGeneratorFunction> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStorageService _storageService;

    public FeedGeneratorFunction(
        ILogger<FeedGeneratorFunction> logger,
        IHttpClientFactory httpClientFactory,
        IStorageService storageService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _storageService = storageService;
    }

    [Function("FeedGeneratorTimer")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Feed generator function triggered at: {Time}", DateTime.UtcNow);

        try
        {
            var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
            var config = new GeneratorConfig();

            // Clean output directory if it exists
            if (Directory.Exists(config.OutputDir))
            {
                Directory.Delete(config.OutputDir, true);
                _logger.LogInformation("Cleaned output directory");
            }

            // Generate feeds and website
            var feedGenerator = new FeedGeneratorService(_httpClientFactory, baseUrl, config);
            var feedCount = await feedGenerator.GenerateAllFeedsAsync("podcasts.json");

            _logger.LogInformation("Generated {FeedCount} feeds", feedCount);

            // Upload to Azure Blob Storage (Static Website)
            await _storageService.UploadDirectoryAsync(config.FullSiteDir);

            _logger.LogInformation("Feed generation and deployment completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feed generation failed: {Message}", ex.Message);
            throw;
        }
    }

    [Function("FeedGeneratorManual")]
    public async Task<string> RunManualAsync(
        [Microsoft.Azure.Functions.Worker.HttpTrigger(
            Microsoft.Azure.Functions.Worker.AuthorizationLevel.Function,
            "post")] Microsoft.Azure.Functions.Worker.Http.HttpRequestData req)
    {
        _logger.LogInformation("Manual feed generation triggered");

        try
        {
            var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
            var config = new GeneratorConfig();

            // Clean output directory if it exists
            if (Directory.Exists(config.OutputDir))
            {
                Directory.Delete(config.OutputDir, true);
            }

            // Generate feeds and website
            var feedGenerator = new FeedGeneratorService(_httpClientFactory, baseUrl, config);
            var feedCount = await feedGenerator.GenerateAllFeedsAsync("podcasts.json");

            _logger.LogInformation("Generated {FeedCount} feeds", feedCount);

            // Upload to Azure Blob Storage (Static Website)
            await _storageService.UploadDirectoryAsync(config.FullSiteDir);

            var message = $"Successfully generated {feedCount} feeds and deployed to Azure Storage";
            _logger.LogInformation(message);

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual feed generation failed: {Message}", ex.Message);
            throw;
        }
    }
}

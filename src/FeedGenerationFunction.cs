using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace DrPodcast;

public class FeedGenerationFunction
{
    private readonly ILogger<FeedGenerationFunction> _logger;
    private readonly IPodcastFeedService _feedService;

    public FeedGenerationFunction(
        ILogger<FeedGenerationFunction> logger,
        IPodcastFeedService feedService)
    {
        _logger = logger;
        _feedService = feedService;
    }

    /// <summary>
    /// Timer-triggered function that runs hourly to generate podcast feeds
    /// </summary>
    [Function("GenerateFeedsTimer")]
    public async Task RunTimer(
        [TimerTrigger("0 0 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        _logger.LogInformation("Feed generation started at: {time}", DateTime.UtcNow);

        try
        {
            await _feedService.GenerateAllFeedsAsync();
            _logger.LogInformation("Feed generation completed successfully at: {time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feed generation failed");
            throw;
        }
    }

    /// <summary>
    /// HTTP-triggered function for manual feed generation
    /// </summary>
    [Function("GenerateFeedsHttp")]
    public async Task<HttpResponseData> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get")] HttpRequestData req,
        FunctionContext context)
    {
        _logger.LogInformation("Manual feed generation triggered");

        try
        {
            await _feedService.GenerateAllFeedsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(@"{""status"":""success"",""message"":""Feeds generated successfully""}");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual feed generation failed");

            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync($@"{{""status"":""error"",""message"":""{ex.Message}""}}");

            return response;
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
        FunctionContext context)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(@"{""status"":""healthy"",""service"":""DrPodcast Feed Generator""}");

        return response;
    }
}

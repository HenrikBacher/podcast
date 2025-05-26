using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ommer.DrApi;
using Polly;
using Polly.Extensions.Http;

namespace Ommer.Client;

public class DrApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    private readonly ILogger<DrApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DrApiClient(HttpClient httpClient, ILogger<DrApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var operation = context.GetValueOrDefault("operation", "HTTP request");
                    _logger.LogWarning("Retry {RetryCount}/3 for {Operation} after {Delay}ms", 
                        retryCount, operation, timespan.TotalMilliseconds);
                });
    }

    public async Task<Show> FetchShowInfoAsync(string apiUri, string urn, string apiKey)
    {
        var url = $"{apiUri}/series/{urn}";
        _logger.LogInformation("Fetching show info from {Url}", url);

        var context = new Context("fetch-show-info");
        var response = await _retryPolicy.ExecuteAsync(async (ctx) =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-apikey", apiKey);
            request.Headers.Add("Accept", "application/json");
            
            var result = await _httpClient.SendAsync(request);
            
            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError("HTTP {StatusCode}: {ReasonPhrase} for {Url}", 
                    result.StatusCode, result.ReasonPhrase, url);
                throw new HttpRequestException($"HTTP {result.StatusCode}: {result.ReasonPhrase}");
            }
            
            return result;
        }, context);

        var json = await response.Content.ReadAsStringAsync();
        var show = JsonSerializer.Deserialize<Show>(json, _jsonOptions) 
            ?? throw new InvalidOperationException("Failed to deserialize show information");
        
        _logger.LogInformation("Successfully fetched show: {Title}", show.Title);
        return show;
    }

    public async Task<List<Item>> FetchEpisodesAsync(string baseUri, string urn, string apiKey)
    {
        var items = new List<Item>();
        var currentUri = $"{AppendPath(baseUri, urn)}/episodes?limit=1024";
        var page = 1;

        try
        {
            while (!string.IsNullOrEmpty(currentUri))
            {
                _logger.LogInformation("Fetching page {Page} from {Uri}", page, currentUri);
                
                var context = new Context($"fetch-episodes-page-{page}");
                var response = await _retryPolicy.ExecuteAsync(async (ctx) =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                    request.Headers.Add("x-apikey", apiKey);
                    request.Headers.Add("Accept", "application/json");
                    
                    var result = await _httpClient.SendAsync(request);
                    
                    if (!result.IsSuccessStatusCode)
                    {
                        _logger.LogError("HTTP {StatusCode}: {ReasonPhrase} for {Url}", 
                            result.StatusCode, result.ReasonPhrase, currentUri);
                        throw new HttpRequestException($"HTTP {result.StatusCode}: {result.ReasonPhrase}");
                    }
                    
                    return result;
                }, context);

                var json = await response.Content.ReadAsStringAsync();
                var episodes = JsonSerializer.Deserialize<Episodes>(json, _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize episodes for page {page}");

                _logger.LogInformation("Retrieved {Count} items from page {Page}", episodes.Items.Count, page);
                items.AddRange(episodes.Items);

                currentUri = episodes.Next;
                page++;
            }

            _logger.LogInformation("Completed fetching all episodes. Total count: {Count}", items.Count);
            return items;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to fetch episodes");
            throw new InvalidOperationException($"Failed to fetch episodes: {e.Message}", e);
        }
    }

    private static string AppendPath(string baseUrl, string suffix)
    {
        return baseUrl.EndsWith('/') ? $"{baseUrl}{suffix}" : $"{baseUrl}/{suffix}";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

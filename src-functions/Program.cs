using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker;
using DrPodcast.Functions;
using Polly;
using Polly.Extensions.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Add HTTP client with Polly retry policy
        services.AddHttpClient("DrApi", client =>
        {
            var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
            client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => !msg.IsSuccessStatusCode)
            .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, _) =>
                    Console.WriteLine($"Retry {retryCount} after {timespan} seconds")));

        // Register services
        services.AddSingleton<IStorageService, BlobStorageService>();
    })
    .Build();

await host.RunAsync();

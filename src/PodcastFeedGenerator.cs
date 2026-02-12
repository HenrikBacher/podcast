using DrPodcast;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

if (args.Contains("--generate"))
{
    // Console mode: generate feeds once and exit (CI/CD behavior)
    var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
    var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
    var config = GeneratorConfig.FromEnvironment();

    ServiceCollection services = [];
    services.AddLogging(b => b.AddConsole());
    services.AddHttpClient("DrApi", client =>
    {
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
    services.AddSingleton<FeedGenerationService>();

    #pragma warning disable ASP0000 // Intentional: console mode uses its own DI container
    await using var serviceProvider = services.BuildServiceProvider();
    #pragma warning restore ASP0000
    var feedService = serviceProvider.GetRequiredService<FeedGenerationService>();

    var podcastsJsonPath = args.SkipWhile(a => a != "--podcasts").Skip(1).FirstOrDefault() ?? "podcasts.json";

    await feedService.GenerateFeedsAsync(podcastsJsonPath, baseUrl, config);
}
else
{
    // Web server mode: serve static feeds + periodic background regeneration
    var builder = WebApplication.CreateBuilder(args);

    var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";

    builder.Services.AddHttpClient("DrApi", client =>
    {
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

    builder.Services.AddSingleton<FeedGenerationService>();
    builder.Services.AddHostedService<FeedRefreshBackgroundService>();

    var app = builder.Build();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

    // Serve static files from the generated site directory
    var config = GeneratorConfig.FromEnvironment();
    Directory.CreateDirectory(config.FullSiteDir);
    Directory.CreateDirectory(config.FeedsDir);

    var contentTypeProvider = new FileExtensionContentTypeProvider();
    contentTypeProvider.Mappings[".xml"] = "application/rss+xml; charset=utf-8";

    var fileProvider = new PhysicalFileProvider(Path.GetFullPath(config.FullSiteDir));

    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider,
        ContentTypeProvider = contentTypeProvider,
        OnPrepareResponse = ctx =>
        {
            var contentType = ctx.Context.Response.ContentType ?? "";

            // Ensure UTF-8 charset on text and JSON responses
            if (!contentType.Contains("charset", StringComparison.OrdinalIgnoreCase)
                && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                    || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Context.Response.ContentType = contentType + "; charset=utf-8";
            }

            // Content-hash ETag — survives atomic rewrites when content is unchanged
            var filePath = ctx.File.PhysicalPath;
            if (filePath is not null && File.Exists(filePath))
            {
                using var stream = File.OpenRead(filePath);
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = Convert.ToHexStringLower(sha.ComputeHash(stream));
                ctx.Context.Response.Headers.ETag = $"\"{hash}\"";
            }

            // Feeds refresh every 15 min — cache 5 min, then revalidate via ETag
            var headers = ctx.Context.Response.GetTypedHeaders();
            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromMinutes(5) };
            else
                headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromHours(1) };
        }
    });

    app.Run();
}

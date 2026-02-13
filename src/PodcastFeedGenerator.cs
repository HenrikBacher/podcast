using DrPodcast;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

if (args.Contains("--generate"))
{
    // Console mode: generate feeds once and exit (CI/CD behavior)
    var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "";
    var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com";
    var config = GeneratorConfig.FromEnvironment();

    ServiceCollection services = [];
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
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
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

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

    builder.Services.AddHttpClient("AudioProxy", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => !msg.IsSuccessStatusCode)
        .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, _) =>
                Console.WriteLine($"AudioProxy retry {retryCount} after {timespan} seconds")));

    builder.Services.AddSingleton<FeedGenerationService>();
    builder.Services.AddHostedService<FeedRefreshBackgroundService>();
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/rss+xml"]);
    });

    var app = builder.Build();

    app.UseResponseCompression();

    // Health check endpoint
    app.MapGet("/health", () => Results.Text("healthy"));

    // Audio proxy endpoint: streams MP4/M4A from DR with corrected Content-Type
    app.MapGet("/proxy/audio", async (HttpContext context, IHttpClientFactory clientFactory) =>
    {
        var url = context.Request.Query["url"].FirstOrDefault();
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Host.EndsWith("dr.dk", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Invalid or disallowed URL.");
        }

        using var client = clientFactory.CreateClient("AudioProxy");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // Forward Range header for seek support
        if (context.Request.Headers.TryGetValue("Range", out var rangeHeader))
        {
            request.Headers.TryAddWithoutValidation("Range", rangeHeader.ToString());
        }

        var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        var statusCode = (int)upstream.StatusCode;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "audio/mp4";

        if (upstream.Content.Headers.ContentLength is { } length)
        {
            context.Response.ContentLength = length;
        }

        if (upstream.Headers.AcceptRanges.Count > 0)
        {
            context.Response.Headers["Accept-Ranges"] = upstream.Headers.AcceptRanges.ToString();
        }

        if (upstream.Content.Headers.ContentRange is { } contentRange)
        {
            context.Response.Headers["Content-Range"] = contentRange.ToString();
        }

        await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);
        await upstreamStream.CopyToAsync(context.Response.Body, context.RequestAborted);

        return Results.Empty;
    });

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
            var headers = ctx.Context.Response.GetTypedHeaders();
            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromMinutes(5) };
            else
                headers.CacheControl = new() { Public = true, MaxAge = TimeSpan.FromHours(1) };
        }
    });

    app.Run();
}

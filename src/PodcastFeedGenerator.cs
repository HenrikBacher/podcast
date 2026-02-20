using DrPodcast;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

// Web server mode: serve static feeds + periodic background regeneration
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var config = GeneratorConfig.FromEnvironment();
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

if (config.PreferMp4)
{
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
}

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

// Audio proxy: streams M4A/MP4 audio from DR with corrected Content-Type (only when PREFER_MP4 is enabled)
if (config.PreferMp4)
{
    app.MapGet("/proxy/audio/{ep}/{asset}", async (string ep, string asset, HttpContext context, IHttpClientFactory clientFactory, ILogger<FeedGenerationService> logger) =>
    {
        if (!RegexCache.HexString().IsMatch(ep) || !RegexCache.HexString().IsMatch(asset))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var upstreamUrl = new Uri($"https://api.dr.dk/radio/v1/assetlinks/urn:dr:radio:episode:{ep}/{asset}");
        var client = clientFactory.CreateClient("AudioProxy");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);

            if (context.Request.Headers.TryGetValue("Range", out var rangeHeader))
                request.Headers.TryAddWithoutValidation("Range", rangeHeader.ToString());

            if (context.Request.Headers.TryGetValue("User-Agent", out var userAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent.ToString());

            using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)upstream.StatusCode;
            context.Response.ContentType = "audio/mp4";

            if (upstream.Content.Headers.ContentLength is { } length)
                context.Response.ContentLength = length;

            if (upstream.Headers.TryGetValues("Accept-Ranges", out var acceptRanges))
                context.Response.Headers["Accept-Ranges"] = string.Join(", ", acceptRanges);

            if (upstream.Content.Headers.ContentRange is { } contentRange)
                context.Response.Headers["Content-Range"] = contentRange.ToString();

            await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);
            await upstreamStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Audio proxy failed for ep={Ep}", ep);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 502;
        }
    });
}

// Serve static files from the generated site directory
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

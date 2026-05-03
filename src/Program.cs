static IAsyncPolicy<HttpResponseMessage> RetryPolicy(string label, ILogger logger) =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (_, timespan, retryCount, _) =>
                logger.LogWarning("{Label} retry {RetryCount} after {Timespan}", label, retryCount, timespan));

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
.AddPolicyHandler((sp, _) => RetryPolicy("DrApi", sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpRetry")));

if (config.PreferMp4)
{
    builder.Services.AddHttpClient("AudioProxy", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .AddPolicyHandler((sp, _) => RetryPolicy("AudioProxy", sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpRetry")));

    // 20 requests/min per IP with no queuing — burst tolerance via 4 segments
    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy("audio-proxy", context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
        options.OnRejected = (context, _) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return ValueTask.CompletedTask;
        };
    });
}

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<DrApiClient>();
builder.Services.AddSingleton<FeedGenerationService>();
builder.Services.AddHostedService<FeedRefreshBackgroundService>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = [..ResponseCompressionDefaults.MimeTypes, "application/xml"];
});

var app = builder.Build();

app.UseResponseCompression();

// Liveness: process is up and serving. Keep this cheap so orchestrators
// don't restart the container just because DR's API is down.
app.MapGet("/health", () => Results.Text("ok"));

// Readiness: feeds exist and are fresh. Route orchestrator readiness probes here.
app.MapGet("/ready", (FeedGenerationService feedService) =>
{
    if (feedService.LastSuccessfulRunUtc is not { } lastRun)
        return Results.Text("not ready: no successful generation yet", statusCode: 503);

    var age = DateTime.UtcNow - lastRun;
    if (age > TimeSpan.FromHours(24))
        return Results.Text($"not ready: last successful run was {age.TotalMinutes:F0}min ago", statusCode: 503);

    return Results.Text("ready");
});

// Audio proxy: streams M4A/MP4 audio from DR with corrected Content-Type (only when PREFER_MP4 is enabled)
if (config.PreferMp4)
{
    app.UseRateLimiter();

    // Forward client headers that affect content selection or caching, so the
    // upstream can answer with a 206 (Range) or 304 (conditional GET) directly.
    string[] forwardClientHeaders = ["Range", "User-Agent", "If-None-Match", "If-Modified-Since", "If-Range"];

    app.MapMethods("/proxy/audio/{ep}/{asset}", ["GET", "HEAD"], async (string ep, string asset, HttpContext context, IHttpClientFactory clientFactory, ILogger<FeedGenerationService> logger) =>
    {
        // Strip optional .m4a suffix added to enclosure URLs for podcatcher compatibility.
        if (asset.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            asset = asset[..^4];

        const int maxHexLength = 64;
        if (ep.Length > maxHexLength || asset.Length > maxHexLength
            || !RegexCache.HexString().IsMatch(ep) || !RegexCache.HexString().IsMatch(asset))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var upstreamUrl = new Uri($"https://api.dr.dk/radio/v1/assetlinks/urn:dr:radio:episode:{ep}/{asset}");
        var client = clientFactory.CreateClient("AudioProxy");
        var isHead = HttpMethods.IsHead(context.Request.Method);

        try
        {
            // Mirror the client's verb upstream so HEAD probes don't pull whole audio bodies from DR.
            using var request = new HttpRequestMessage(isHead ? HttpMethod.Head : HttpMethod.Get, upstreamUrl);

            foreach (var name in forwardClientHeaders)
            {
                if (context.Request.Headers.TryGetValue(name, out var value))
                    request.Headers.TryAddWithoutValidation(name, value.ToString());
            }

            using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)upstream.StatusCode;
            var isSuccess = (int)upstream.StatusCode is >= 200 and < 300;

            // Only override Content-Type to audio/mp4 on success — error bodies (404 JSON,
            // 502 HTML) must keep their actual type so clients/proxies don't misread them.
            if (isSuccess)
            {
                context.Response.ContentType = "audio/mp4";
            }
            else if (upstream.Content.Headers.ContentType is { } upstreamCt)
            {
                context.Response.ContentType = upstreamCt.ToString();
            }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (upstream.Content.Headers.ContentLength is { } length)
                context.Response.ContentLength = length;

            if (upstream.Headers.TryGetValues("Accept-Ranges", out var acceptRanges))
                context.Response.Headers["Accept-Ranges"] = string.Join(", ", acceptRanges);

            if (upstream.Content.Headers.ContentRange is { } contentRange)
                context.Response.Headers["Content-Range"] = contentRange.ToString();

            // Forward caching/validator headers so podcatchers can issue conditional
            // requests on subsequent fetches and DR returns 304s instead of full bodies.
            if (upstream.Headers.ETag is { } etag)
                context.Response.Headers.ETag = etag.ToString();

            if (upstream.Content.Headers.LastModified is { } lastModified)
                context.Response.Headers.LastModified = lastModified.ToString("R");

            if (upstream.Headers.CacheControl is { } cacheControl)
            {
                context.Response.Headers.CacheControl = cacheControl.ToString();
            }
            else if (isSuccess)
            {
                // Audio assets are content-addressable by their hash and effectively immutable.
                // When DR is silent, give intermediary CDNs and clients a sensible cacheable default.
                context.Response.Headers.CacheControl = "public, max-age=3600, immutable";
            }

            if (upstream.Content.Headers.Expires is { } expires)
                context.Response.Headers.Expires = expires.ToString("R");

            // Always advertise that responses vary by Range/If-None-Match so downstream caches
            // don't merge bodies across conditional or partial requests, even if upstream is silent.
            var varyHeaders = upstream.Headers.Vary.Count > 0
                ? string.Join(", ", upstream.Headers.Vary.Concat(new[] { "Range", "If-None-Match" }).Distinct(StringComparer.OrdinalIgnoreCase))
                : "Range, If-None-Match";
            context.Response.Headers.Vary = varyHeaders;

            // HEAD must not have a body; 304 Not Modified must not have a body either.
            if (isHead || upstream.StatusCode == System.Net.HttpStatusCode.NotModified)
                return;

            await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);
            await upstreamStream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Audio proxy failed for ep={Ep}", ep);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 502;
        }
    }).RequireRateLimiting("audio-proxy");
}

// Serve static files from the generated site directory
Directory.CreateDirectory(config.FullSiteDir);
Directory.CreateDirectory(config.FeedsDir);

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".xml"] = "application/xml; charset=utf-8";

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

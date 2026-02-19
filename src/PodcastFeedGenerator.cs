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
    app.MapGet("/proxy/audio", async (HttpContext context, IHttpClientFactory clientFactory) =>
    {
        var path = context.Request.Query["path"].FirstOrDefault();
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/'))
            return Results.BadRequest("Invalid path.");

        var upstreamUrl = new Uri($"https://api.dr.dk{path}");

        using var client = clientFactory.CreateClient("AudioProxy");
        using var request = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);

        if (context.Request.Headers.TryGetValue("Range", out var rangeHeader))
            request.Headers.TryAddWithoutValidation("Range", rangeHeader.ToString());

        var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)upstream.StatusCode;
        context.Response.ContentType = "audio/mp4";

        if (upstream.Content.Headers.ContentLength is { } length)
            context.Response.ContentLength = length;

        if (upstream.Headers.AcceptRanges.Count > 0)
            context.Response.Headers["Accept-Ranges"] = upstream.Headers.AcceptRanges.ToString();

        if (upstream.Content.Headers.ContentRange is { } contentRange)
            context.Response.Headers["Content-Range"] = contentRange.ToString();

        await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(context.RequestAborted);
        await upstreamStream.CopyToAsync(context.Response.Body, context.RequestAborted);

        return Results.Empty;
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

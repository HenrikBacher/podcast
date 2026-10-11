using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

// Web server mode: serve static feeds + periodic background regeneration
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.UseUtcTimestamp = true;
    // Shows the per-podcast "Slug: …" scope, so anything logged while processing a podcast
    // (DrApiClient's messages included) says which one.
    options.IncludeScopes = true;
});

var config = GeneratorConfig.FromEnvironment();
DrApiClient.Register(builder.Services, GeneratorConfig.RequireApiKey());

// Feeds are XML and compress by roughly 80% — worth it for a server that exists to be polled.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    // Explicit list rather than the defaults: this covers exactly what
    // MinimalContentTypeProvider serves. Parameters like "; charset=utf-8" are ignored
    // during matching, so the bare media types are what belong here.
    options.MimeTypes = [
        "application/xml", "text/xml", "text/html", "text/css",
        "application/javascript", "application/json", "image/svg+xml", "text/plain",
    ];
});
// The default is Fastest. Optimal (Brotli quality 4) is ~10% smaller for ~15–30 ms more on a 3.4 MB feed, and
// only full downloads pay it: most feed requests are conditional and end in a bodiless 304.
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<FeedGenerationService>();
builder.Services.AddHostedService<FeedRefreshBackgroundService>();

var app = builder.Build();

// Every feed link (atom:self, the website's copy and app links) is built from BASE_URL, so a value
// without a scheme quietly produces broken links. Say so at startup.
if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var baseUri) || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
    app.Logger.LogWarning("BASE_URL '{BaseUrl}' isn't an absolute http(s) URL; feed links will be broken.", config.BaseUrl);

// Liveness: process is up and serving. Keep this cheap so orchestrators
// don't restart the container just because DR's API is down.
app.MapGet("/health", () => Results.Text("ok"));

// Readiness: feeds exist and are fresh. Route orchestrator readiness probes here.
app.MapGet("/ready", (FeedGenerationService feedService) =>
{
    if (feedService.LastSuccessfulRunUtc is not { } lastRun)
        return Results.Text("not ready: no successful generation yet", statusCode: 503);

    var age = DateTime.UtcNow - lastRun;
    if (age > config.ReadinessStaleAfter)
        return Results.Text($"not ready: last successful run was {age.TotalMinutes:F0}min ago", statusCode: 503);

    return Results.Text("ready");
});

// Serve static files from the generated site directory
Directory.CreateDirectory(config.FullSiteDir);
Directory.CreateDirectory(config.FeedsDir);

var contentTypeProvider = new MinimalContentTypeProvider();

var fileProvider = new PhysicalFileProvider(Path.GetFullPath(config.FullSiteDir));

// The site source ships in the image, so the stylesheet and script hash is fixed for the process.
var assetVersion = WebsiteGenerator.AssetVersion(config.SiteSourceDir);

app.UseResponseCompression();
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    ContentTypeProvider = contentTypeProvider,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.GetTypedHeaders().CacheControl = CachePolicy.For(ctx.Context.Request, ctx.Context.Response.ContentType, assetVersion);
    }
});

app.Run();

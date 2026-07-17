static void AddRetryHandler(IHttpClientBuilder httpBuilder) =>
    httpBuilder.AddStandardResilienceHandler();

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
});

var config = GeneratorConfig.FromEnvironment();
var apiKey = Environment.GetEnvironmentVariable("API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("API_KEY environment variable is not set.");

AddRetryHandler(builder.Services.AddHttpClient("DrApi", client =>
{
    client.DefaultRequestHeaders.Add("X-Apikey", apiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
}));

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<DrApiClient>();
builder.Services.AddSingleton<FeedGenerationService>();
builder.Services.AddHostedService<FeedRefreshBackgroundService>();

var app = builder.Build();

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

// Serve static files from the generated site directory
Directory.CreateDirectory(config.FullSiteDir);
Directory.CreateDirectory(config.FeedsDir);

var contentTypeProvider = new MinimalContentTypeProvider();

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

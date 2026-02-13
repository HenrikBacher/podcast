using DrPodcast;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

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

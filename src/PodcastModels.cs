namespace DrPodcast;

[JsonSerializable(typeof(PodcastList))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(List<Episode>))]
[JsonSerializable(typeof(EpisodesPage))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PodcastJsonContext : JsonSerializerContext
{
}

public record EpisodesPage(List<Episode>? Items, string? Next);

public record FeedMetadata(string Slug, string Title, string? ImageUrl);

public record GeneratorConfig(
    string OutputDir = "output",
    string SiteDir = "_site",
    string SiteSourceDir = "site",
    string BaseUrl = "https://example.com",
    int RefreshIntervalMinutes = GeneratorConfig.DefaultRefreshIntervalMinutes
)
{
    public const int DefaultRefreshIntervalMinutes = 15;

    // Bounded so the backoff arithmetic stays in range and a typo can't park the refresh
    // loop for months.
    private const int MaxRefreshIntervalMinutes = 24 * 60;

    public string FullSiteDir => Path.Combine(OutputDir, SiteDir);
    public string FeedsDir => Path.Combine(FullSiteDir, "feeds");

    /// <summary>
    /// How stale the last successful run may be before readiness reports 503. Derived from the
    /// refresh interval so the probe actually tracks the refresh loop, with a floor that tolerates
    /// a few missed ticks on a short interval.
    /// </summary>
    public TimeSpan ReadinessStaleAfter =>
        TimeSpan.FromMinutes(Math.Max(RefreshIntervalMinutes * 4, 60));

    /// <summary>Every environment variable this app reads is resolved here.</summary>
    public static GeneratorConfig FromEnvironment() => new(
        BaseUrl: Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com",
        RefreshIntervalMinutes: ParseRefreshInterval(Environment.GetEnvironmentVariable("REFRESH_INTERVAL_MINUTES"))
    );

    internal static int ParseRefreshInterval(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mins) && mins > 0
            ? Math.Min(mins, MaxRefreshIntervalMinutes)
            : DefaultRefreshIntervalMinutes;

    /// <summary>Reads the DR API key. Kept off the record so the secret isn't held in DI state.</summary>
    public static string RequireApiKey() =>
        Environment.GetEnvironmentVariable("API_KEY") is { } key && !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new InvalidOperationException("API_KEY environment variable is not set.");
}

public record PodcastList
{
    public List<Podcast> Podcasts { get; init; } = [];
}

public record Podcast(string Slug, string Urn, List<ImageAsset>? ImageAssets);

public record Episode(
    string? Title,
    string? Description,
    string? PublishTime,
    string? StartTime,
    string? Id,
    string? PresentationUrl,
    int? DurationMilliseconds,
    List<AudioAsset>? AudioAssets,
    List<string>? Categories,
    List<ImageAsset>? ImageAssets,
    int? EpisodeNumber,
    int? SeasonNumber,
    bool ExplicitContent,
    long? Order
);

public record AudioAsset(string? Format, int? Bitrate, string? Url, long? FileSize);

public record ImageAsset(string? Id, string? Target, string? Ratio);

public record Series(
    List<string>? Categories,
    int NumberOfSeries,
    string? PresentationType,
    string? LatestEpisodeStartTime,
    string? PresentationUrl,
    bool ExplicitContent,
    string? DefaultOrder,
    string? Title,
    string? Punchline,
    string? Description,
    List<ImageAsset>? ImageAssets
);

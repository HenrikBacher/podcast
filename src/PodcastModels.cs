namespace DrPodcast;

[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(List<Episode>))]
[JsonSerializable(typeof(ApiPage<Episode>))]
[JsonSerializable(typeof(ApiPage<Series>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PodcastJsonContext : JsonSerializerContext
{
}

/// <summary>One page of a DR API listing (episodes of a series, or all series).</summary>
public record ApiPage<T>(List<T>? Items, string? Next, int? TotalSize, int? Offset);

/// <summary>What the website lists about a feed. Compared by value to decide when index.html is rebuilt.</summary>
public record FeedMetadata(
    string Slug,
    string Title,
    string? ImageUrl,
    string? Subtitle = null,
    // Joined with '|' rather than a list, so record equality compares the contents.
    string? Categories = null,
    int? EpisodeCount = null,
    string? LatestEpisodeTitle = null,
    DateTime? LatestEpisodeUtc = null,
    // When the series' first episode came out, set only for podcasts in their own right
    // (Series.IsStandalonePodcast), so the website can mark new ones.
    DateTime? FirstEpisodeUtc = null,
    string? Description = null);

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

/// <param name="Series">The series as discovery listed it, which saves fetching it again; null means fetch.</param>
public record Podcast(string Slug, string Urn, Series? Series = null);

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
    List<ImageAsset>? ImageAssets,
    int? NumberOfSeasons = null,
    string? Id = null,
    string? Slug = null,
    string? ProductionNumber = null,
    string? PodcastUrl = null
)
{
    // DR reports a seasonal show's season count as either numberOfSeries or numberOfSeasons,
    // depending on the series ("laegens-bord" only has the latter).
    [JsonIgnore]
    public bool HasSeasons => NumberOfSeries > 0 || NumberOfSeasons > 0;

    // A podcast in its own right: DR publishes its own podcast feed for it, or presents it as a show.
    // Most other series are live radio programmes, which DR keeps online for only a few weeks, so their
    // oldest available episode says nothing about when they started.
    [JsonIgnore]
    public bool IsStandalonePodcast => !string.IsNullOrEmpty(PodcastUrl) || PresentationType == "Show";
}

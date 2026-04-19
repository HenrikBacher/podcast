namespace DrPodcast;

[JsonSerializable(typeof(PodcastList))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(List<Episode>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PodcastJsonContext : JsonSerializerContext
{
}

public record FeedMetadata(string Slug, string Title, string? ImageUrl);

public record GeneratorConfig(
    string OutputDir = "output",
    string SiteDir = "_site",
    string SiteSourceDir = "site",
    bool PreferMp4 = false,
    string BaseUrl = "https://example.com"
)
{
    public string FullSiteDir => Path.Combine(OutputDir, SiteDir);
    public string FeedsDir => Path.Combine(FullSiteDir, "feeds");

    public static GeneratorConfig FromEnvironment() => new GeneratorConfig(
        PreferMp4: Environment.GetEnvironmentVariable("PREFER_MP4")?.ToLower() is "true" or "1",
        BaseUrl: Environment.GetEnvironmentVariable("BASE_URL") ?? "https://example.com"
    );
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
    string? Type,
    List<string>? Categories,
    int NumberOfEpisodes,
    int NumberOfSeries,
    int NumberOfSeasons,
    string? PresentationType,
    string? GroupingType,
    string? LatestEpisodeStartTime,
    string? PresentationUrl,
    bool ExplicitContent,
    string? DefaultOrder,
    string? Id,
    string? Slug,
    string? Title,
    string? Punchline,
    string? Description,
    List<ImageAsset>? ImageAssets
);

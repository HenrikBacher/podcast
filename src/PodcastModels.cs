namespace DrPodcast;

[JsonSerializable(typeof(PodcastList))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(List<Episode>))]
[JsonSerializable(typeof(FeedManifest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PodcastJsonContext : JsonSerializerContext
{
}

public record FeedMetadata(
    string Slug,
    string Title,
    string? ImageUrl
);

public record GeneratorConfig(
    string OutputDir = "output",
    string SiteDir = "_site",
    string SiteSourceDir = "site",
    bool PreferMp4 = false
)
{
    public string FullSiteDir => Path.Combine(OutputDir, SiteDir);
    public string FeedsDir => Path.Combine(FullSiteDir, "feeds");

    public static GeneratorConfig FromEnvironment() => new GeneratorConfig(
        PreferMp4: Environment.GetEnvironmentVariable("PREFER_MP4") is "true" or "1"
    );
}

public record FeedManifest(
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("feedCount")] int FeedCount,
    [property: JsonPropertyName("feeds")] List<FeedFileInfo> Feeds
);

public record FeedFileInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("title")] string Title
);

public record PodcastList
{
    [JsonPropertyName("podcasts")]
    public List<Podcast> Podcasts { get; init; } = [];
}

public record Podcast(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("urn")] string Urn,
    [property: JsonPropertyName("imageAssets")] List<ImageAsset>? ImageAssets
);

public record Episode(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("publishTime")] string? PublishTime,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("presentationUrl")] string? PresentationUrl,
    [property: JsonPropertyName("durationMilliseconds")] int? DurationMilliseconds,
    [property: JsonPropertyName("audioAssets")] List<AudioAsset>? AudioAssets,
    [property: JsonPropertyName("categories")] List<string>? Categories,
    [property: JsonPropertyName("imageAssets")] List<ImageAsset>? ImageAssets,
    [property: JsonPropertyName("episodeNumber")] int? EpisodeNumber,
    [property: JsonPropertyName("seasonNumber")] int? SeasonNumber,
    [property: JsonPropertyName("explicitContent")] bool ExplicitContent,
    [property: JsonPropertyName("order")] long? Order
);

public record AudioAsset(
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("bitrate")] int? Bitrate,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("fileSize")] int? FileSize
);

public record ImageAsset(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("ratio")] string? Ratio
);

public record Series(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("categories")] List<string>? Categories,
    [property: JsonPropertyName("numberOfEpisodes")] int NumberOfEpisodes,
    [property: JsonPropertyName("numberOfSeries")] int NumberOfSeries,
    [property: JsonPropertyName("numberOfSeasons")] int NumberOfSeasons,
    [property: JsonPropertyName("presentationType")] string? PresentationType,
    [property: JsonPropertyName("groupingType")] string? GroupingType,
    [property: JsonPropertyName("latestEpisodeStartTime")] string? LatestEpisodeStartTime,
    [property: JsonPropertyName("presentationUrl")] string? PresentationUrl,
    [property: JsonPropertyName("explicitContent")] bool ExplicitContent,
    [property: JsonPropertyName("defaultOrder")] string? DefaultOrder,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("punchline")] string? Punchline,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("imageAssets")] List<ImageAsset>? ImageAssets
);

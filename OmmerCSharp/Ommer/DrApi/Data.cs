using System.Text.Json.Serialization;

namespace Ommer.DrApi;

public record Group(
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("totalSize")] int TotalSize,
    [property: JsonPropertyName("next")] string? Next,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("groupId")] string GroupId,
    [property: JsonPropertyName("items")] List<Item> Items
);

public record Show(
    [property: JsonPropertyName("categories")] List<string> Categories,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("imageAssets")] List<ImageAsset> ImageAssets,
    [property: JsonPropertyName("isAvailableOnDemand")] string? IsAvailableOnDemand,
    [property: JsonPropertyName("latestEpisodeStartTime")] string LatestEpisodeStartTime,
    [property: JsonPropertyName("learnId")] string? LearnId,
    [property: JsonPropertyName("numberOfEpisodes")] string? NumberOfEpisodes,
    [property: JsonPropertyName("ocsUrn")] string? OcsUrn,
    [property: JsonPropertyName("podcastUrl")] string? PodcastUrl,
    [property: JsonPropertyName("presentationType")] string? PresentationType,
    [property: JsonPropertyName("presentationUrl")] string PresentationUrl,
    [property: JsonPropertyName("productionNumber")] string? ProductionNumber,
    [property: JsonPropertyName("psdbSlug")] string? PsdbSlug,
    [property: JsonPropertyName("psdbUrn")] string? PsdbUrn,
    [property: JsonPropertyName("punchline")] string? Punchline,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("sortLetter")] string? SortLetter,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("visualIdentity")] VisualIdentity? VisualIdentity
);

public record VisualIdentity(
    [property: JsonPropertyName("gradient")] Gradient? Gradient
);

public record Gradient(
    [property: JsonPropertyName("colors")] List<string>? Colors
);

public record ImageAsset(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("ratio")] string Ratio,
    [property: JsonPropertyName("format")] string Format
);

public record Episodes(
    [property: JsonPropertyName("items")] List<Item> Items,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("previous")] string? Previous,
    [property: JsonPropertyName("next")] string? Next,
    [property: JsonPropertyName("self")] string? Self,
    [property: JsonPropertyName("totalSiz")] int TotalSize
);

public record Item(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("durationMilliseconds")] long DurationMilliseconds,
    [property: JsonPropertyName("productionNumber")] string ProductionNumber,
    [property: JsonPropertyName("audioAssets")] List<AudioAsset> AudioAssets,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("presentationUrl")] string PresentationUrl,
    [property: JsonPropertyName("publishTime")] string PublishTime
);

public record AudioAsset(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("isStreamLive")] bool IsStreamLive,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("bitrate")] int Bitrate,
    [property: JsonPropertyName("fileSize")] long FileSize,
    [property: JsonPropertyName("url")] string Url
);

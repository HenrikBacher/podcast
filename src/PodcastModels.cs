using System.Text.Json.Serialization;

namespace DrPodcast;

public record PodcastList
{
    [JsonPropertyName("podcasts")]
    public List<Podcast> Podcasts { get; init; } = [];
}

public record Podcast
{
    [JsonPropertyName("slug")]
    public string Slug { get; init; } = string.Empty;

    [JsonPropertyName("urn")]
    public string Urn { get; init; } = string.Empty;

    [JsonPropertyName("imageAssets")]
    public List<ImageAsset>? ImageAssets { get; init; }
}

public record Episode
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("publishTime")]
    public string? PublishTime { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("presentationUrl")]
    public string? PresentationUrl { get; init; }

    [JsonPropertyName("durationMilliseconds")]
    public int? DurationMilliseconds { get; init; }

    [JsonPropertyName("audioAssets")]
    public List<AudioAsset>? AudioAssets { get; init; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; init; }

    [JsonPropertyName("imageAssets")]
    public List<ImageAsset>? ImageAssets { get; init; }

    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; init; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; init; }
    
    [JsonPropertyName("explicitContent")]
    public bool ExplicitContent { get; init; }
}

public record AudioAsset
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("bitrate")]
    public int? Bitrate { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("fileSize")]
    public int? FileSize { get; init; }
}

public record ImageAsset
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    [JsonPropertyName("ratio")]
    public string? Ratio { get; init; }
}

public record Channel
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("link")]
    public string? Link { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; init; }

    [JsonPropertyName("lastBuildDate")]
    public string? LastBuildDate { get; init; }

    [JsonPropertyName("explicit")]
    public string? Explicit { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("block")]
    public string? Block { get; init; }

    [JsonPropertyName("owner")]
    public ChannelOwner? Owner { get; init; }

    [JsonPropertyName("new-feed-url")]
    public string? NewFeedUrl { get; init; }

    [JsonPropertyName("image")]
    public string? Image { get; init; }
}

public record ChannelOwner
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public record Series
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; init; }

    [JsonPropertyName("numberOfEpisodes")]
    public int NumberOfEpisodes { get; init; }

    [JsonPropertyName("numberOfSeries")]
    public int NumberOfSeries { get; init; }

    [JsonPropertyName("numberOfSeasons")]
    public int NumberOfSeasons { get; init; }

    [JsonPropertyName("presentationType")]
    public string? PresentationType { get; init; }

    [JsonPropertyName("groupingType")]
    public string? GroupingType { get; init; }

    [JsonPropertyName("latestEpisodeStartTime")]
    public string? LatestEpisodeStartTime { get; init; }

    [JsonPropertyName("presentationUrl")]
    public string? PresentationUrl { get; init; }

    [JsonPropertyName("explicitContent")]
    public bool ExplicitContent { get; init; }

    [JsonPropertyName("defaultOrder")]
    public string? DefaultOrder { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("punchline")]
    public string? Punchline { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("imageAssets")]
    public List<ImageAsset>? ImageAssets { get; init; }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DrPodcast
{
    public class PodcastList
    {
        [JsonPropertyName("podcasts")]
        public List<Podcast> Podcasts { get; set; } = new();
    }

    public class Podcast
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("urn")]
        public string Urn { get; set; } = string.Empty;

        [JsonPropertyName("imageAssets")]
        public List<ImageAsset>? ImageAssets { get; set; }
    }

    public class Episode
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("publishTime")]
        public string? PublishTime { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("presentationUrl")]
        public string? PresentationUrl { get; set; }

        [JsonPropertyName("durationMilliseconds")]
        public int? DurationMilliseconds { get; set; }

        [JsonPropertyName("audioAssets")]
        public List<AudioAsset>? AudioAssets { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("imageAssets")]
        public List<ImageAsset>? ImageAssets { get; set; }

        [JsonPropertyName("episodeNumber")]
        public int? EpisodeNumber { get; set; }

        [JsonPropertyName("seasonNumber")]
        public int? SeasonNumber { get; set; }
    }

    public class AudioAsset
    {
        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("bitrate")]
        public int? Bitrate { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("fileSize")]
        public int? FileSize { get; set; }
    }

    public class ImageAsset
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("target")]
        public string? Target { get; set; }

        [JsonPropertyName("ratio")]
        public string? Ratio { get; set; }
    }

    public class Channel
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("copyright")]
        public string? Copyright { get; set; }

        [JsonPropertyName("lastBuildDate")]
        public string? LastBuildDate { get; set; }

        [JsonPropertyName("explicit")]
        public string? Explicit { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("block")]
        public string? Block { get; set; }

        [JsonPropertyName("owner")]
        public ChannelOwner? Owner { get; set; }

        [JsonPropertyName("new-feed-url")]
        public string? NewFeedUrl { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }
    }

    public class ChannelOwner
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class Series
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("numberOfEpisodes")]
        public int NumberOfEpisodes { get; set; }

        [JsonPropertyName("numberOfSeries")]
        public int NumberOfSeries { get; set; }

        [JsonPropertyName("numberOfSeasons")]
        public int NumberOfSeasons { get; set; }

        [JsonPropertyName("presentationType")]
        public string? PresentationType { get; set; }

        [JsonPropertyName("groupingType")]
        public string? GroupingType { get; set; }

        [JsonPropertyName("latestEpisodeStartTime")]
        public string? LatestEpisodeStartTime { get; set; }

        [JsonPropertyName("presentationUrl")]
        public string? PresentationUrl { get; set; }

        [JsonPropertyName("explicitContent")]
        public bool ExplicitContent { get; set; }

        [JsonPropertyName("defaultOrder")]
        public string? DefaultOrder { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("punchline")]
        public string? Punchline { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("imageAssets")]
        public List<ImageAsset>? ImageAssets { get; set; }

    }
}

namespace DrPodcast;

internal static partial class RegexCache
{
    [GeneratedRegex(@"\s*\([^)]*feed[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    public static partial Regex FeedTitleCleanup();

    [GeneratedRegex(@"^/radio/v\d+/assetlinks/urn:dr:radio:episode:(?<ep>[0-9a-f]+)/(?<asset>[0-9a-f]+)$")]
    public static partial Regex DrAssetUrl();

    [GeneratedRegex(@"^[0-9a-f]+$")]
    public static partial Regex HexString();

    [GeneratedRegex(@"limit=(\d+)")]
    public static partial Regex LimitParameter();
}

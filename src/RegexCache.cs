namespace DrPodcast;

internal static partial class RegexCache
{
    [GeneratedRegex(@"^/radio/v\d+/assetlinks/urn:dr:radio:episode:(?<ep>[0-9a-f]+)/(?<asset>[0-9a-f]+)$")]
    public static partial Regex DrAssetUrl();
}

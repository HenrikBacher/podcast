namespace DrPodcast;

public static class PodcastHelpers
{
    /// <summary>
    /// The image at a given width from DR's image server, which serves fixed sizes (320, 640, 1024, 2048,
    /// and the 3000×3000 original) and rounds a requested width up to the next one.
    /// </summary>
    public static string WithImageWidth(string imageUrl, int width) =>
        $"{imageUrl}{(imageUrl.Contains('?') ? '&' : '?')}imwidth={width}";

    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        // Priority: Podcast 1:1 (4) > Default 1:1 (3) > Podcast any (2) > Default any (1) > any other
        // target (0). An unrecognised target (DR also sends "SquareImage") is a last resort: still better
        // than a channel with no image.
        var bestAsset = imageAssets
            .Where(a => a != null)
            .MaxBy(Rank);

        if (string.IsNullOrEmpty(bestAsset?.Id)
            || bestAsset.Id.Contains("..")
            || bestAsset.Id.IndexOfAny(['/', '?', '#', '\\', '\r', '\n']) >= 0)
            return null;

        return $"https://asset.dr.dk/drlyd/images/{bestAsset.Id}";
    }

    // Runs once per image asset per episode across every podcast, so compare in place rather
    // than allocating a lowercased copy of Target for each one.
    private static int Rank(ImageAsset asset)
    {
        var isSquare = string.Equals(asset.Ratio, "1:1", StringComparison.Ordinal);

        if (string.Equals(asset.Target, "podcast", StringComparison.OrdinalIgnoreCase))
            return isSquare ? 4 : 2;

        if (string.Equals(asset.Target, "default", StringComparison.OrdinalIgnoreCase))
            return isSquare ? 3 : 1;

        return 0;
    }
}

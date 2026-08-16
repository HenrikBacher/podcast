namespace DrPodcast;

public static class PodcastHelpers
{
    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        // Priority: Podcast 1:1 (4) > Default 1:1 (3) > Podcast any (2) > Default any (1) > none (0)
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

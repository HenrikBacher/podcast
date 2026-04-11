namespace DrPodcast;

public static class PodcastHelpers
{
    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        // Calculate priority for each asset (single pass):
        // Priority: Podcast 1:1 (4) > Default 1:1 (3) > Podcast any (2) > Default any (1) > none (0)
        var bestAsset = imageAssets
            .Where(a => a != null)
            .MaxBy(a =>
            {
                var isPodcast = a.Target?.Equals("podcast", StringComparison.OrdinalIgnoreCase) ?? false;
                var isDefault = a.Target?.Equals("default", StringComparison.OrdinalIgnoreCase) ?? false;
                var isSquare = a.Ratio == "1:1";

                return isPodcast && isSquare ? 4
                     : isDefault && isSquare ? 3
                     : isPodcast ? 2
                     : isDefault ? 1
                     : 0;
            });

        if (string.IsNullOrEmpty(bestAsset?.Id)
            || bestAsset.Id.Contains("..")
            || bestAsset.Id.IndexOfAny(['?', '#', '\\', '\r', '\n']) >= 0)
            return null;

        return $"https://asset.dr.dk/drlyd/images/{bestAsset.Id}";
    }
}

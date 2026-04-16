namespace DrPodcast;

public static class PodcastHelpers
{
    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        // Priority: Podcast 1:1 (4) > Default 1:1 (3) > Podcast any (2) > Default any (1) > none (0)
        var bestAsset = imageAssets
            .Where(a => a != null)
            .MaxBy(a => (a.Target?.ToLowerInvariant(), a.Ratio) switch
            {
                ("podcast", "1:1") => 4,
                ("default", "1:1") => 3,
                ("podcast", _)     => 2,
                ("default", _)     => 1,
                _                  => 0,
            });

        if (string.IsNullOrEmpty(bestAsset?.Id)
            || bestAsset.Id.Contains("..")
            || bestAsset.Id.IndexOfAny(['?', '#', '\\', '\r', '\n']) >= 0)
            return null;

        return $"https://asset.dr.dk/drlyd/images/{bestAsset.Id}";
    }
}

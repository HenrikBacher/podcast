namespace DrPodcast;

public static class PodcastHelpers
{
    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        // Priority order: podcast 1:1, default 1:1, podcast any ratio, default any ratio
        var imageId = imageAssets.FirstOrDefault(a => a?.Target?.Equals("podcast", StringComparison.OrdinalIgnoreCase) == true && a.Ratio == "1:1")?.Id
                      ?? imageAssets.FirstOrDefault(a => a?.Target?.Equals("default", StringComparison.OrdinalIgnoreCase) == true && a.Ratio == "1:1")?.Id
                      ?? imageAssets.FirstOrDefault(a => a?.Target?.Equals("podcast", StringComparison.OrdinalIgnoreCase) == true)?.Id
                      ?? imageAssets.FirstOrDefault(a => a?.Target?.Equals("default", StringComparison.OrdinalIgnoreCase) == true)?.Id;

        return !string.IsNullOrEmpty(imageId) ? $"https://asset.dr.dk/drlyd/images/{imageId}" : null;
    }
}

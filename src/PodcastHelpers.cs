namespace DrPodcast;

public static class PodcastHelpers
{
    public static string MapToPodcastCategory(string category)
    {
        return category switch
        {
            "Dokumentar" => "Documentary",
            "Historie" => "History",
            "Sundhed" => "Health & Fitness",
            "Samfund" => "Society & Culture",
            "Videnskab og tech" => "Science",
            "Tro og eksistens" => "Religion & Spirituality",
            "Kriminal" => "True Crime",
            "Kultur" => "Society & Culture",
            "Nyheder" => "News",
            "Underholdning" => "Entertainment",
            "Sport" => "Sports",
            "Musik" => "Music",
            _ => category
        };
    }

    public static string? GetImageUrlFromAssets(List<ImageAsset>? imageAssets)
    {
        if (imageAssets is not { Count: > 0 }) return null;

        var img = imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "podcast", StringComparison.OrdinalIgnoreCase) && a?.Ratio == "1:1") ??
                  imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "default", StringComparison.OrdinalIgnoreCase) && a?.Ratio == "1:1") ??
                  imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "podcast", StringComparison.OrdinalIgnoreCase)) ??
                  imageAssets.FirstOrDefault(a => string.Equals(a?.Target, "default", StringComparison.OrdinalIgnoreCase));

        return img?.Id is { } imgId && !string.IsNullOrEmpty(imgId)
            ? $"https://asset.dr.dk/drlyd/images/{imgId}"
            : null;
    }
}

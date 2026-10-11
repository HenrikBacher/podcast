namespace DrPodcast;

/// <summary>Turns DR's series listing into the podcasts to publish, each with the slug of its feed URL.</summary>
public static class PodcastCatalog
{
    public static PodcastList Build(IEnumerable<Series> series, ILogger? logger = null)
    {
        var podcasts = new List<Podcast>();
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        // Ordered by id, so if two series ever derive the same slug the same one keeps it on every run.
        foreach (var s in series.Where(s => !string.IsNullOrEmpty(s.Id) && !IsNewsBulletin(s)).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (SlugFor(s) is not { } slug)
            {
                logger?.LogWarning("Skipping series {Urn}: no usable slug in {Slug}.", s.Id, s.Slug);
                continue;
            }
            if (taken.TryGetValue(slug, out var owner))
            {
                logger?.LogWarning("Series {Urn} would share the slug {Slug} with {Owner}; publishing it as {Slug}-{ProductionNumber}.",
                    s.Id, slug, owner, slug, s.ProductionNumber);
                slug = $"{slug}-{s.ProductionNumber}";
                if (!IsSafeSlug(slug) || taken.ContainsKey(slug))
                    continue;
            }
            taken[slug] = s.Id!;
            podcasts.Add(new Podcast(slug, s.Id!, s));
        }
        return new PodcastList { Podcasts = podcasts };
    }

    // Feed URLs are /feeds/{slug}.xml and podcatchers are subscribed to them, so a slug must stay stable.
    // It is DR's own slug without its "-{productionNumber}" suffix, the part DR keeps fixed.
    // Hourly news bulletins aren't published: DR types exactly Radioavisen and the P4 regional news
    // ("P4 … regionale nyheder") as News.
    internal static bool IsNewsBulletin(Series series) => series.PresentationType == "News";

    internal static string? SlugFor(Series series)
    {
        var slug = series.Slug;
        var suffix = $"-{series.ProductionNumber}";
        if (slug is not null && !string.IsNullOrEmpty(series.ProductionNumber) && slug.Length > suffix.Length && slug.EndsWith(suffix, StringComparison.Ordinal))
            slug = slug[..^suffix.Length];

        // The slug becomes a file name and a URL path.
        return slug is not null && IsSafeSlug(slug) ? slug : null;
    }

    private static bool IsSafeSlug(string slug) =>
        slug.Length > 0 && slug[0] != '-' && slug[^1] != '-' && slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}

using Microsoft.Net.Http.Headers;

namespace DrPodcast;

/// <summary>Cache-Control for the static site.</summary>
internal static class CachePolicy
{
    private static readonly TimeSpan OneYear = TimeSpan.FromDays(365);

    /// <param name="assetVersion">The hash index.html puts on the stylesheet and script URLs.</param>
    public static CacheControlHeaderValue For(HttpRequest request, string? contentType, string assetVersion)
    {
        contentType ??= "";

        // Feeds, and index.html, which changes whenever a feed does: revalidated on every use, so a new
        // episode is seen as soon as it's published, at the cost of a 304 when nothing changed. Leaving
        // Cache-Control out instead would let caches guess a lifetime from Last-Modified, typically a tenth
        // of the time since the last change: about a day for a feed that has been quiet for ten.
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            return new() { Public = true, NoCache = true };

        // A URL whose content can never change: the stylesheet and script at the current hash, and fonts,
        // which are never edited in place (a changed font gets a new file name).
        var versioned = request.Query["v"] == assetVersion
            && (contentType.StartsWith("text/css", StringComparison.OrdinalIgnoreCase) || contentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase));
        if (versioned || request.Path.StartsWithSegments("/fonts", StringComparison.OrdinalIgnoreCase))
            return new() { Public = true, MaxAge = OneYear, Extensions = { new NameValueHeaderValue("immutable") } };

        return new() { Public = true, MaxAge = TimeSpan.FromHours(1) };
    }
}

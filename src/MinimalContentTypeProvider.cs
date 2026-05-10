using Microsoft.AspNetCore.StaticFiles;

namespace DrPodcast;

internal sealed class MinimalContentTypeProvider : IContentTypeProvider
{
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xml"] = "application/xml; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public bool TryGetContentType(string subpath, out string contentType)
    {
        var ext = Path.GetExtension(subpath);
        if (!string.IsNullOrEmpty(ext) && Mappings.TryGetValue(ext, out var ct))
        {
            contentType = ct;
            return true;
        }
        contentType = null!;
        return false;
    }
}

namespace SiteScan.Application.Crawling;

public sealed class UrlCanonicalizer : IUrlCanonicalizer
{
    public Uri? TryCanonicalize(Uri baseUri, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;

        // Ignore non-http(s) hrefs early (mailto:, javascript:, tel:, data:)
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Uri.TryCreate(baseUri, href, out var resolved))
            return null;

        if (!resolved.IsAbsoluteUri) return null;
        if (resolved.Scheme is not ("http" or "https")) return null;

        return CanonicalizeAbsolute(resolved);
    }

    public Uri CanonicalizeAbsolute(Uri absolute)
    {
        var b = new UriBuilder(absolute);

        // Lowercase host
        b.Host = b.Host.ToLowerInvariant();

        // Remove fragment
        b.Fragment = string.Empty;

        // Remove default ports
        if ((b.Scheme == "http" && b.Port == 80) || (b.Scheme == "https" && b.Port == 443))
            b.Port = -1;

        // Normalize empty path -> "/"
        if (string.IsNullOrEmpty(b.Path))
            b.Path = "/";

        // Best-effort collapse duplicate slashes in path (do NOT touch query)
        b.Path = CollapseDuplicateSlashes(b.Path);

        return b.Uri;
    }

    public string GetDedupeKey(Uri canonical)
    {
        // Key decision: keep query as-is, preserve path casing from server (Uri uses normalized escaping).
        // Host lowercased already.
        // You can include scheme if you want to treat http/https as distinct.
        return canonical.AbsoluteUri;
    }

    private static string CollapseDuplicateSlashes(string path)
    {
        if (path.Length < 2) return path;

        Span<char> buffer = stackalloc char[path.Length];
        int w = 0;
        bool prevSlash = false;

        foreach (var ch in path)
        {
            if (ch == '/')
            {
                if (prevSlash) continue;
                prevSlash = true;
            }
            else prevSlash = false;

            buffer[w++] = ch;
        }

        return new string(buffer[..w]);
    }
}

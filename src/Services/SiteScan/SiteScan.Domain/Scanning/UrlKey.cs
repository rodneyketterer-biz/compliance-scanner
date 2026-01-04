namespace SiteScan.Domain.Scanning;

public static class UrlKey
{
    // Must match your intake normalization rules; this is a safe “key” for lookup/dedupe/query.
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim();

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Host = uri.Host.ToLowerInvariant()
        };

        // normalize default ports
        if ((builder.Scheme == "http" && builder.Port == 80) ||
            (builder.Scheme == "https" && builder.Port == 443))
            builder.Port = -1;

        // empty path -> /
        if (string.IsNullOrWhiteSpace(builder.Path))
            builder.Path = "/";

        return builder.Uri.ToString();
    }
}
using System.Text;

namespace SiteScan.Domain.UrlResolution;

/// <summary>
/// Canonicalization rules:
/// - host lowercased
/// - fragment removed
/// - default ports removed
/// - empty path => "/"
/// - collapse duplicate slashes in path (best-effort)
/// - query preserved verbatim from input (no reordering), unless redirect changes it
/// - trailing slash rule:
///     * root stays "/"
///     * otherwise preserve whatever the (final) server-provided path is (we do NOT add/remove a trailing slash)
/// </summary>
public static class UrlCanonicalizer
{
    public static CanonicalizeOutcome CanonicalizeFromUserInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return CanonicalizeOutcome.Fail(
                UrlErrorCode.InvalidAbsoluteUri,
                "URL must be a non-empty absolute URI (including scheme).");
        }

        // Ensure parseable absolute URI (explicitly reject missing scheme)
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            // If it looks like missing scheme (common case), call it out explicitly
            if (LooksLikeMissingScheme(input))
            {
                return CanonicalizeOutcome.Fail(
                    UrlErrorCode.MissingScheme,
                    "URL is missing a scheme. Only http:// and https:// are supported.");
            }

            return CanonicalizeOutcome.Fail(
                UrlErrorCode.InvalidAbsoluteUri,
                "URL is not a valid absolute URI.");
        }

        return CanonicalizeUri(uri, originalText: input);
    }

    public static CanonicalizeOutcome CanonicalizeUri(Uri uri, string? originalText = null)
    {
        // Scheme allowlist
        var scheme = uri.Scheme;
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return CanonicalizeOutcome.Fail(
                UrlErrorCode.MissingScheme,
                "URL is missing a scheme. Only http:// and https:// are supported.");
        }

        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalizeOutcome.Fail(
                UrlErrorCode.UnsupportedScheme,
                $"Unsupported URL scheme '{scheme}'. Only http and https are allowed.");
        }

        // Reject credentials (user:pass@)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return CanonicalizeOutcome.Fail(
                UrlErrorCode.ContainsCredentials,
                "URLs containing credentials (user:pass@) are not allowed.");
        }

        // Query must be preserved exactly as provided.
        // Uri.Query may be normalized by System.Uri in some cases,
        // so we best-effort extract it verbatim from original text when available.
        var queryVerbatim = ExtractQueryVerbatim(originalText ?? uri.OriginalString);

        // Build normalized path
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (string.IsNullOrEmpty(path))
            path = "/";

        path = CollapseDuplicateSlashes(path);

        // Host lowercased
        var host = uri.Host.ToLowerInvariant();

        // Remove default ports
        var port = uri.IsDefaultPort ? -1 : uri.Port;
        if (scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && port == 80) port = -1;
        if (scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && port == 443) port = -1;

        var ub = new UriBuilder(uri)
        {
            Scheme = scheme.ToLowerInvariant(),
            Host = host,
            Port = port,
            Fragment = string.Empty, // remove fragment
            Path = path
        };

        // IMPORTANT: UriBuilder.Query expects no leading '?'
        ub.Query = queryVerbatim ?? string.Empty;

        // UriBuilder may insert "/" when Path empty; we already handle it.
        var canonical = ub.Uri;

        return CanonicalizeOutcome.Ok(canonical);
    }

    private static bool LooksLikeMissingScheme(string input)
    {
        // Very small heuristic: contains a dot and no "://"
        return !input.Contains("://", StringComparison.Ordinal) &&
               input.Contains('.', StringComparison.Ordinal) &&
               !input.StartsWith("/", StringComparison.Ordinal);
    }

    private static string CollapseDuplicateSlashes(string path)
    {
        // Best-effort collapse of duplicate slashes in the path portion only.
        if (string.IsNullOrEmpty(path)) return "/";

        var sb = new StringBuilder(path.Length);
        char prev = '\0';
        foreach (var c in path)
        {
            if (c == '/' && prev == '/')
            {
                // skip duplicate
            }
            else
            {
                sb.Append(c);
            }
            prev = c;
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "/" : result;
    }

    private static string? ExtractQueryVerbatim(string input)
    {
        // Preserve EXACT substring between '?' and '#'(or end), without reordering.
        // Return without leading '?' (UriBuilder.Query style).
        if (string.IsNullOrEmpty(input)) return null;

        var q = input.IndexOf('?', StringComparison.Ordinal);
        if (q < 0) return null;

        var hash = input.IndexOf('#', q + 1);
        var end = hash >= 0 ? hash : input.Length;

        // empty query like "http://x/?" => verbatim becomes ""
        var query = input.Substring(q + 1, end - (q + 1));
        return query;
    }
}

public sealed record CanonicalizeOutcome
{
    public Uri? CanonicalUri { get; init; }
    public UrlResolutionError? Error { get; init; }
    public bool Success => Error is null;

    public static CanonicalizeOutcome Ok(Uri canonical) => new() { CanonicalUri = canonical };
    public static CanonicalizeOutcome Fail(UrlErrorCode code, string message) => new()
    {
        Error = new UrlResolutionError(code, message)
    };
}

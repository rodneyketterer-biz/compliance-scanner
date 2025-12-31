using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Crawling;

public interface ICrawlRecordWriter
{
    Task WriteAsync(CrawlRecord record, CancellationToken ct);
}

public interface ICrawlRecordReader
{
    Task<IReadOnlyList<CrawlRecord>> GetByScanIdAsync(ScanId scanId, CancellationToken ct);
}

public interface IUrlCanonicalizer
{
    // Normalizes candidate URL (remove fragment, lowercase host, resolve relative, remove default ports, etc.)
    Uri? TryCanonicalize(Uri baseUri, string href);
    Uri CanonicalizeAbsolute(Uri absolute);
    string GetDedupeKey(Uri canonical);
}

public interface IScopePolicy
{
    bool IsInScope(Uri candidateCanonical, Uri scanRootCanonical, out string? notes);
}

public interface IRobotsPolicy
{
    Task<RobotsDecision> CanFetchAsync(Uri url, CancellationToken ct);
}

public sealed record RobotsDecision(bool Allowed, string? Notes, IReadOnlyList<Uri> Sitemaps);

public interface IHtmlLinkExtractor
{
    // Accepts HTML bytes and returns raw href/src candidates (may be relative)
    IReadOnlyList<string> ExtractLinks(ReadOnlySpan<byte> htmlBytes);
}

public interface IHttpFetcher
{
    // Fetches with redirects enabled (HttpClient default). Returns final Uri.
    Task<FetchResult> FetchAsync(Uri url, CancellationToken ct);
}

public sealed record FetchResult(
    Uri RequestedUrl,
    Uri FinalUrl,
    int StatusCode,
    string? ContentType,
    byte[] Body
);

public interface IPolitenessGate
{
    Task WaitTurnAsync(Uri url, CancellationToken ct);
}

// SiteScan.Domain/Scanning/CrawlRecord.cs
namespace SiteScan.Domain.Scanning;

public sealed record CrawlRecord(
    ScanId ScanId,
    Uri Url,               // final URL for fetched, or candidate URL for skipped
    Uri? FinalUrl,         // set for fetched (after redirects)
    int? StatusCode,
    string? ContentType,
    DateTimeOffset TimestampUtc,
    int Depth,
    CrawlDisposition Disposition,
    SkipReason SkipReason,
    string? Notes          // optional: details (e.g., which scope rule, robots rule, etc.)
);

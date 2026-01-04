using System.Security.Cryptography;
using System.Text;

namespace SiteScan.Domain.Scanning;

public sealed class PageSnapshot
{
    private PageSnapshot() { } // EF

    public PageSnapshot(
        PageSnapshotId id,
        ScanId scanId,
        string finalUrl,
        int statusCode,
        string? contentType,
        DateTimeOffset fetchedAtUtc,
        int crawlDepth,
        SnapshotContent? content,
        SnapshotHeaders headers,
        SnapshotIntegrity integrity)
    {
        Id = id;
        ScanId = scanId;

        FinalUrl = finalUrl;
        NormalizedUrlKey = UrlKey.Normalize(finalUrl);

        StatusCode = statusCode;
        ContentType = contentType;
        FetchedAtUtc = fetchedAtUtc;
        CrawlDepth = crawlDepth;

        Content = content;        // null when not text/html OR when failed
        Headers = headers;
        Integrity = integrity;

        if (content is null && integrity.ContentHashSha256 is not null)
            throw new InvalidOperationException("Content hash must be null when no content is stored.");
    }

    public PageSnapshotId Id { get; private set; }
    public ScanId ScanId { get; private set; }

    public string FinalUrl { get; private set; } = default!;
    public string NormalizedUrlKey { get; private set; } = default!; // stable lookup by URL

    public int StatusCode { get; private set; }
    public string? ContentType { get; private set; }
    public DateTimeOffset FetchedAtUtc { get; private set; }
    public int CrawlDepth { get; private set; }

    // null if not HTML or failure
    public SnapshotContent? Content { get; private set; }

    public SnapshotHeaders Headers { get; private set; } = default!;
    public SnapshotIntegrity Integrity { get; private set; } = default!;
}
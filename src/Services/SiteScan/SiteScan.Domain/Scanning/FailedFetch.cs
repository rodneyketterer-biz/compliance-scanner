namespace SiteScan.Domain.Scanning;

public sealed class FailedFetch
{
    private FailedFetch() { } // EF

    public FailedFetch(
        Guid id,
        ScanId scanId,
        string url,
        DateTimeOffset failedAtUtc,
        int crawlDepth,
        FetchFailureReason reason,
        string message)
    {
        Id = id;
        ScanId = scanId;
        Url = url;
        NormalizedUrlKey = UrlKey.Normalize(url);
        FailedAtUtc = failedAtUtc;
        CrawlDepth = crawlDepth;
        Reason = reason;
        Message = message;
    }

    public Guid Id { get; private set; }
    public ScanId ScanId { get; private set; }

    public string Url { get; private set; } = default!;
    public string NormalizedUrlKey { get; private set; } = default!;

    public DateTimeOffset FailedAtUtc { get; private set; }
    public int CrawlDepth { get; private set; }

    public FetchFailureReason Reason { get; private set; }
    public string Message { get; private set; } = default!;
}
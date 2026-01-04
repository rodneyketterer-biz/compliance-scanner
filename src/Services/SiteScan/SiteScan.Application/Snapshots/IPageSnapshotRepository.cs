using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Snapshots;

public interface IPageSnapshotRepository
{
    Task AddSnapshotAsync(PageSnapshot snapshot, CancellationToken ct);
    Task AddFailureAsync(FailedFetch failure, CancellationToken ct);

    Task<PageSnapshot?> GetSnapshotAsync(ScanId scanId, string pageUrl, CancellationToken ct);
    Task<IReadOnlyList<PageSnapshotListItem>> ListPagesAsync(ScanId scanId, CancellationToken ct);

    Task<SnapshotHeaders?> GetHeadersAsync(ScanId scanId, string pageUrl, CancellationToken ct);

    /// <summary>Delete snapshots/failures older than cutoff, including stored HTML blobs.</summary>
    Task<RetentionDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct);
}

public sealed record PageSnapshotListItem(
    Guid SnapshotId,
    string FinalUrl,
    int StatusCode,
    string? ContentType,
    DateTimeOffset FetchedAtUtc,
    int CrawlDepth,
    bool HasHtml,
    bool WasTruncated);

public sealed record RetentionDeleteResult(int SnapshotsDeleted, int FailuresDeleted);
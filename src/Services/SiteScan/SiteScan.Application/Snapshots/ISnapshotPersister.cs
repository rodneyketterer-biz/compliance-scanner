using SiteScan.Application.Crawling;
using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Snapshots;

/// <summary>
/// Port through which the crawler persists a fetched page's HTML, response headers,
/// and metadata, or records that a fetch failed.
/// </summary>
public interface ISnapshotPersister
{
    /// <summary>
    /// Persist a successfully fetched page.
    /// HTML content is stored and hashed; non-HTML pages store metadata and headers only.
    /// </summary>
    Task PersistSuccessAsync(
        ScanId scanId,
        FetchResult fetch,
        int crawlDepth,
        CancellationToken ct);

    /// <summary>
    /// Record a failed fetch.  No HTML snapshot is produced; the failure reason
    /// and message are stored for audit and rule-evaluation use.
    /// </summary>
    Task PersistFailureAsync(
        ScanId scanId,
        string url,
        DateTimeOffset failedAtUtc,
        int crawlDepth,
        FetchFailureReason reason,
        string message,
        CancellationToken ct);
}

/// <summary>
/// No-op implementation for use in tests and in contexts where snapshot
/// persistence is not required (e.g., unit tests of the crawl engine itself).
/// </summary>
public sealed class NullSnapshotPersister : ISnapshotPersister
{
    public static readonly NullSnapshotPersister Instance = new();

    private NullSnapshotPersister() { }

    public Task PersistSuccessAsync(ScanId scanId, FetchResult fetch, int crawlDepth, CancellationToken ct)
        => Task.CompletedTask;

    public Task PersistFailureAsync(ScanId scanId, string url, DateTimeOffset failedAtUtc, int crawlDepth, FetchFailureReason reason, string message, CancellationToken ct)
        => Task.CompletedTask;
}

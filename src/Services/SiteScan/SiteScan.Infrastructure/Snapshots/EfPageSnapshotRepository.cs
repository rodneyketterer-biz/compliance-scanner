using Microsoft.EntityFrameworkCore;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.Scanning;
using SiteScan.Infrastructure.Persistence;

namespace SiteScan.Infrastructure.Snapshots;

public sealed class EfPageSnapshotRepository : IPageSnapshotRepository
{
    private readonly SiteScanDbContext _db;
    private readonly IHtmlSnapshotStorage _storage;

    public EfPageSnapshotRepository(SiteScanDbContext db, IHtmlSnapshotStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task AddSnapshotAsync(PageSnapshot snapshot, CancellationToken ct)
    {
        _db.PageSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddFailureAsync(FailedFetch failure, CancellationToken ct)
    {
        _db.FailedFetches.Add(failure);
        await _db.SaveChangesAsync(ct);
    }

    public Task<PageSnapshot?> GetSnapshotAsync(ScanId scanId, string pageUrl, CancellationToken ct)
    {
        var key = UrlKey.Normalize(pageUrl);

        // Includes owned types by default
        return _db.PageSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScanId == scanId && x.NormalizedUrlKey == key, ct);
    }

    public async Task<IReadOnlyList<PageSnapshotListItem>> ListPagesAsync(ScanId scanId, CancellationToken ct)
    {
        return await _db.PageSnapshots
            .AsNoTracking()
            .Where(x => x.ScanId == scanId)
            .OrderBy(x => x.CrawlDepth)
            .ThenBy(x => x.FinalUrl)
            .Select(x => new PageSnapshotListItem(
                x.Id.Value,
                x.FinalUrl,
                x.StatusCode,
                x.ContentType,
                x.FetchedAtUtc,
                x.CrawlDepth,
                x.Content != null,
                x.Content != null && x.Content.WasTruncated))
            .ToListAsync(ct);
    }

    public async Task<SnapshotHeaders?> GetHeadersAsync(ScanId scanId, string pageUrl, CancellationToken ct)
    {
        var key = UrlKey.Normalize(pageUrl);

        return await _db.PageSnapshots
            .AsNoTracking()
            .Where(x => x.ScanId == scanId && x.NormalizedUrlKey == key)
            .Select(x => x.Headers)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<RetentionDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        // load snapshot storage refs to delete
        var oldSnapshots = await _db.PageSnapshots
            .Where(x => x.FetchedAtUtc < cutoffUtc)
            .Select(x => new { x.Id, StorageRef = x.Content != null ? x.Content.StorageRef : null })
            .ToListAsync(ct);

        foreach (var s in oldSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(s.StorageRef))
                await _storage.DeleteAsync(s.StorageRef!, ct);
        }

        var snapshotsDeleted = oldSnapshots.Count;

        _db.PageSnapshots.RemoveRange(_db.PageSnapshots.Where(x => x.FetchedAtUtc < cutoffUtc));
        var failuresDeleted = await _db.FailedFetches.CountAsync(x => x.FailedAtUtc < cutoffUtc, ct);
        _db.FailedFetches.RemoveRange(_db.FailedFetches.Where(x => x.FailedAtUtc < cutoffUtc));

        await _db.SaveChangesAsync(ct);

        return new RetentionDeleteResult(snapshotsDeleted, failuresDeleted);
    }
}
using System.Security.Cryptography;
using SiteScan.Application.Crawling;
using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Snapshots;

/// <summary>
/// Persists a fetched page's HTML snapshot, response headers, and metadata after
/// each crawl fetch.  Called by the crawler via <see cref="ISnapshotPersister"/>.
///
/// <b>Storage decisions (documented):</b>
/// <list type="bullet">
///   <item>HTML is stored as raw bytes (UTF-8 in practice; the byte stream from
///         HttpClient is stored verbatim so the original encoding is preserved).</item>
///   <item>When <see cref="SnapshotOptions.UseHeaderAllowlist"/> is <c>true</c>
///         only the headers in <see cref="HeadersAllowlist.RuleHeaders"/> are kept
///         (CSP, HSTS, X-Frame-Options, X-Content-Type-Options, Referrer-Policy,
///         ETag, Content-Type).  Set the option to <c>false</c> to store the full
///         header set.</item>
///   <item>The content hash is computed over the <em>stored</em> (possibly
///         truncated) bytes, not the original, so it is always consistent with
///         what is retrievable from storage.</item>
///   <item>Retention is controlled by <see cref="SnapshotOptions.Retention"/>
///         (default 30 days).  Expired snapshots are removed by calling
///         <see cref="IPageSnapshotRepository.DeleteOlderThanAsync"/>; no
///         scheduled job is wired up yet (tracked as a known gap).</item>
/// </list>
/// </summary>
public sealed class SnapshotPersister : ISnapshotPersister
{
    private readonly IPageSnapshotRepository _repo;
    private readonly IHtmlSnapshotStorage _storage;
    private readonly SnapshotOptions _options;

    public SnapshotPersister(
        IPageSnapshotRepository repo,
        IHtmlSnapshotStorage storage,
        SnapshotOptions options)
    {
        _repo = repo;
        _storage = storage;
        _options = options;
    }

    // ── ISnapshotPersister ──────────────────────────────────────────────────

    public async Task PersistSuccessAsync(
        ScanId scanId,
        FetchResult fetch,
        int crawlDepth,
        CancellationToken ct)
    {
        var fetchedAtUtc = DateTimeOffset.UtcNow;

        var headersToStore = FilterHeaders(
            fetch.ResponseHeaders ?? new Dictionary<string, string>(),
            _options.UseHeaderAllowlist);
        var snapshotHeaders = new SnapshotHeaders(headersToStore);

        var isHtml = fetch.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;

        if (isHtml && fetch.Body.Length > 0)
        {
            var originalLen = (long)fetch.Body.Length;
            var max = _options.MaxHtmlBytesPerPage;

            byte[] storedBytes;
            bool truncated;

            if (fetch.Body.Length > max)
            {
                storedBytes = fetch.Body.AsSpan(0, max).ToArray();
                truncated = true;
            }
            else
            {
                storedBytes = fetch.Body;
                truncated = false;
            }

            // Hash computed over stored (possibly truncated) bytes for consistency.
            var contentHash = ComputeSha256Hex(storedBytes);

            var snapshotId = PageSnapshotId.New();
            var storageRef = await _storage.SaveAsync(scanId.Value, snapshotId.Value, storedBytes, ct);

            var content = new SnapshotContent(
                storageRef: storageRef,
                originalLengthBytes: originalLen,
                storedLengthBytes: storedBytes.LongLength,
                wasTruncated: truncated);

            var integrity = new SnapshotIntegrity(
                etag: snapshotHeaders.Get("ETag"),
                contentHashSha256: contentHash);

            var snapshot = new PageSnapshot(
                id: snapshotId,
                scanId: scanId,
                finalUrl: fetch.FinalUrl.AbsoluteUri,
                statusCode: fetch.StatusCode,
                contentType: fetch.ContentType,
                fetchedAtUtc: fetchedAtUtc,
                crawlDepth: crawlDepth,
                content: content,
                headers: snapshotHeaders,
                integrity: integrity);

            await _repo.AddSnapshotAsync(snapshot, ct);
            return;
        }

        // Non-HTML: persist metadata + headers only; no HTML blob stored.
        {
            var snapshotId = PageSnapshotId.New();

            var integrity = new SnapshotIntegrity(
                etag: snapshotHeaders.Get("ETag"),
                contentHashSha256: null);

            var snapshot = new PageSnapshot(
                id: snapshotId,
                scanId: scanId,
                finalUrl: fetch.FinalUrl.AbsoluteUri,
                statusCode: fetch.StatusCode,
                contentType: fetch.ContentType,
                fetchedAtUtc: fetchedAtUtc,
                crawlDepth: crawlDepth,
                content: null,
                headers: snapshotHeaders,
                integrity: integrity);

            await _repo.AddSnapshotAsync(snapshot, ct);
        }
    }

    public Task PersistFailureAsync(
        ScanId scanId,
        string url,
        DateTimeOffset failedAtUtc,
        int crawlDepth,
        FetchFailureReason reason,
        string message,
        CancellationToken ct)
    {
        var failure = new FailedFetch(
            id: Guid.NewGuid(),
            scanId: scanId,
            url: url,
            failedAtUtc: failedAtUtc,
            crawlDepth: crawlDepth,
            reason: reason,
            message: message);

        return _repo.AddFailureAsync(failure, ct);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static Dictionary<string, string> FilterHeaders(
        IReadOnlyDictionary<string, string> headers,
        bool allowlist)
    {
        if (!allowlist)
            return new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        var allowed = new HashSet<string>(HeadersAllowlist.RuleHeaders, StringComparer.OrdinalIgnoreCase);
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in headers)
        {
            if (allowed.Contains(k))
                filtered[k] = v;
        }

        return filtered;
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

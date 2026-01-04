using System.Security.Cryptography;
using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Snapshots;

/*This service is what the crawler calls after each fetch to persist the results
 to the database and HTML storage.*/
public sealed class SnapshotPersister
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

    public async Task PersistSuccessAsync(
        ScanId scanId,
        string finalUrl,
        int statusCode,
        string? contentType,
        DateTimeOffset fetchedAtUtc,
        int crawlDepth,
        IReadOnlyDictionary<string, string> responseHeaders,
        byte[]? responseBodyBytes,                 // raw bytes from HttpClient
        CancellationToken ct)
    {
        // headers: store allowlisted or full
        var headersToStore = FilterHeaders(responseHeaders, _options.UseHeaderAllowlist);
        var snapshotHeaders = new SnapshotHeaders(headersToStore);

        SnapshotContent? content = null;
        string? contentHash = null;

        var isHtml = contentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;

        if (isHtml && responseBodyBytes is not null)
        {
            var originalLen = responseBodyBytes.LongLength;
            var max = _options.MaxHtmlBytesPerPage;

            var storedBytes = responseBodyBytes;
            var truncated = false;

            if (storedBytes.Length > max)
            {
                storedBytes = storedBytes.AsSpan(0, max).ToArray();
                truncated = true;
            }

            // hash of stored content (not original) — consistent with "stored content hash"
            contentHash = ComputeSha256Hex(storedBytes);

            var snapshotId = PageSnapshotId.New();
            var storageRef = await _storage.SaveAsync(scanId.Value, snapshotId.Value, storedBytes, ct);

            content = new SnapshotContent(
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
                finalUrl: finalUrl,
                statusCode: statusCode,
                contentType: contentType,
                fetchedAtUtc: fetchedAtUtc,
                crawlDepth: crawlDepth,
                content: content,
                headers: snapshotHeaders,
                integrity: integrity);

            await _repo.AddSnapshotAsync(snapshot, ct);
            return;
        }

        // Non-HTML: store metadata + headers, but no HTML snapshot content.
        {
            var snapshotId = PageSnapshotId.New();

            var integrity = new SnapshotIntegrity(
                etag: snapshotHeaders.Get("ETag"),
                contentHashSha256: null);

            var snapshot = new PageSnapshot(
                id: snapshotId,
                scanId: scanId,
                finalUrl: finalUrl,
                statusCode: statusCode,
                contentType: contentType,
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

    private static Dictionary<string, string> FilterHeaders(
        IReadOnlyDictionary<string, string> headers,
        bool allowlist)
    {
        if (!allowlist)
            return new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        var set = new HashSet<string>(HeadersAllowlist.RuleHeaders, StringComparer.OrdinalIgnoreCase);
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in headers)
        {
            if (set.Contains(k))
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
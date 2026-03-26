using NUnit.Framework;
using SiteScan.Application.Crawling;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.Scanning;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SiteScan.UnitTests.Application;

// ── In-file spy doubles ──────────────────────────────────────────────────────

/// <summary>
/// Spy repository that captures every Add call for assertion.
/// </summary>
internal sealed class SpySnapshotRepository : IPageSnapshotRepository
{
    public readonly List<PageSnapshot> Snapshots = new();
    public readonly List<FailedFetch> Failures = new();

    public Task AddSnapshotAsync(PageSnapshot snapshot, CancellationToken ct)
    {
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task AddFailureAsync(FailedFetch failure, CancellationToken ct)
    {
        Failures.Add(failure);
        return Task.CompletedTask;
    }

    // Query methods not exercised by SnapshotPersister; throw to catch accidental calls.
    public Task<PageSnapshot?> GetSnapshotAsync(ScanId scanId, string pageUrl, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PageSnapshotListItem>> ListPagesAsync(ScanId scanId, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<SnapshotHeaders?> GetHeadersAsync(ScanId scanId, string pageUrl, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<RetentionDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
        => throw new NotSupportedException();
}

/// <summary>
/// Spy HTML storage that holds saved blobs in memory.
/// </summary>
internal sealed class SpyHtmlStorage : IHtmlSnapshotStorage
{
    public readonly Dictionary<string, byte[]> Saved = new(StringComparer.Ordinal);

    public Task<string> SaveAsync(Guid scanId, Guid snapshotId, ReadOnlyMemory<byte> htmlBytes, CancellationToken ct)
    {
        var key = $"{scanId:N}/{snapshotId:N}.html";
        Saved[key] = htmlBytes.ToArray();
        return Task.FromResult(key);
    }

    public Task<ReadOnlyMemory<byte>> ReadAsync(string storageRef, CancellationToken ct)
    {
        if (Saved.TryGetValue(storageRef, out var bytes))
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        throw new KeyNotFoundException($"Storage ref not found: {storageRef}");
    }

    public Task DeleteAsync(string storageRef, CancellationToken ct)
    {
        Saved.Remove(storageRef);
        return Task.CompletedTask;
    }
}

// ── Test class ───────────────────────────────────────────────────────────────

[TestFixture]
public sealed class SnapshotPersisterTests
{
    // ── Shared helpers ──────────────────────────────────────────────────────

    private static FetchResult HtmlFetch(
        string url,
        string html,
        Dictionary<string, string>? headers = null)
    {
        var uri = new Uri(url);
        return new FetchResult(
            RequestedUrl: uri,
            FinalUrl: uri,
            StatusCode: 200,
            ContentType: "text/html; charset=utf-8",
            Body: Encoding.UTF8.GetBytes(html),
            ResponseHeaders: headers ?? new Dictionary<string, string>());
    }

    private static (SnapshotPersister, SpySnapshotRepository, SpyHtmlStorage) Build(
        SnapshotOptions? options = null)
    {
        var repo    = new SpySnapshotRepository();
        var storage = new SpyHtmlStorage();
        var opts    = options ?? new SnapshotOptions();
        return (new SnapshotPersister(repo, storage, opts), repo, storage);
    }

    private static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ── Test 1 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_HtmlPage_StoresSnapshotAndContent()
    {
        // Arrange
        var (persister, repo, storage) = Build();
        var scanId = ScanId.New();
        var fetch  = HtmlFetch("https://example.com/", "<html><body>Hello</body></html>");

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 0, CancellationToken.None);

        // Assert — one snapshot stored with non-null Content and a SHA-256 hash
        Assert.That(repo.Snapshots, Has.Count.EqualTo(1));
        var snap = repo.Snapshots[0];
        Assert.That(snap.FinalUrl,    Is.EqualTo("https://example.com/"));
        Assert.That(snap.StatusCode,  Is.EqualTo(200));
        Assert.That(snap.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        Assert.That(snap.CrawlDepth,  Is.EqualTo(0));
        Assert.That(snap.Content,     Is.Not.Null);
        Assert.That(snap.Content!.WasTruncated, Is.False);
        Assert.That(snap.Integrity.ContentHashSha256, Is.Not.Null.And.Length.EqualTo(64));

        // HTML blob was saved
        Assert.That(storage.Saved, Has.Count.EqualTo(1));
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_HtmlExceedsMaxBytes_TruncatesAndSetsFlag()
    {
        // Arrange — limit to 10 bytes; body is larger
        var (persister, repo, storage) = Build(new SnapshotOptions { MaxHtmlBytesPerPage = 10 });
        var scanId   = ScanId.New();
        var longHtml = new string('a', 200); // 200 bytes in UTF-8
        var fetch    = HtmlFetch("https://example.com/big", longHtml);

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 1, CancellationToken.None);

        // Assert
        var snap = repo.Snapshots[0];
        Assert.That(snap.Content!.WasTruncated,       Is.True);
        Assert.That(snap.Content.StoredLengthBytes,   Is.EqualTo(10));
        Assert.That(snap.Content.OriginalLengthBytes, Is.GreaterThan(10));

        var storedBlob = storage.Saved.Values;
        Assert.That(storedBlob, Has.One.With.Length.EqualTo(10));
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_NonHtmlPage_StoresMetadataOnly()
    {
        // Arrange — CSS content type
        var (persister, repo, storage) = Build();
        var scanId = ScanId.New();
        var uri    = new Uri("https://example.com/style.css");
        var fetch  = new FetchResult(
            RequestedUrl:    uri,
            FinalUrl:        uri,
            StatusCode:      200,
            ContentType:     "text/css",
            Body:            Encoding.UTF8.GetBytes("body { color: red; }"),
            ResponseHeaders: new Dictionary<string, string>());

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 1, CancellationToken.None);

        // Assert — snapshot stored but Content is null; no HTML blob saved
        Assert.That(repo.Snapshots, Has.Count.EqualTo(1));
        var snap = repo.Snapshots[0];
        Assert.That(snap.Content,                     Is.Null);
        Assert.That(snap.Integrity.ContentHashSha256, Is.Null);
        Assert.That(storage.Saved,                    Is.Empty);
    }

    // ── Test 4 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_AllowlistEnabled_FiltersHeaders()
    {
        // Arrange — send a non-allowlisted header plus an allowlisted one
        var (persister, repo, _) = Build(new SnapshotOptions { UseHeaderAllowlist = true });
        var scanId = ScanId.New();
        var headers = new Dictionary<string, string>
        {
            ["Content-Security-Policy"] = "default-src 'self'",
            ["Strict-Transport-Security"] = "max-age=31536000",
            ["X-Powered-By"] = "should-be-filtered"
        };
        var fetch = HtmlFetch("https://example.com/", "<html/>", headers);

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 0, CancellationToken.None);

        // Assert — allowlisted headers stored; non-allowlisted dropped
        var snap = repo.Snapshots[0];
        Assert.That(snap.Headers.Get("Content-Security-Policy"),  Is.Not.Null);
        Assert.That(snap.Headers.Get("Strict-Transport-Security"), Is.Not.Null);
        Assert.That(snap.Headers.Get("X-Powered-By"),             Is.Null);
    }

    // ── Test 5 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_AllowlistDisabled_StoresAllHeaders()
    {
        // Arrange — UseHeaderAllowlist = false
        var (persister, repo, _) = Build(new SnapshotOptions { UseHeaderAllowlist = false });
        var scanId = ScanId.New();
        var headers = new Dictionary<string, string>
        {
            ["Content-Security-Policy"] = "default-src 'self'",
            ["X-Custom-Internal"] = "custom-value"
        };
        var fetch = HtmlFetch("https://example.com/", "<html/>", headers);

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 0, CancellationToken.None);

        // Assert — non-allowlisted header is present because allowlist is disabled
        var snap = repo.Snapshots[0];
        Assert.That(snap.Headers.Get("X-Custom-Internal"), Is.EqualTo("custom-value"));
    }

    // ── Test 6 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_ETagPresent_PopulatesIntegrity()
    {
        // Arrange
        var (persister, repo, _) = Build();
        var scanId  = ScanId.New();
        var headers = new Dictionary<string, string> { ["ETag"] = "\"v1-abc123\"" };
        var fetch   = HtmlFetch("https://example.com/", "<html/>", headers);

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 0, CancellationToken.None);

        // Assert — ETag propagated into SnapshotIntegrity
        Assert.That(repo.Snapshots[0].Integrity.ETag, Is.EqualTo("\"v1-abc123\""));
    }

    // ── Test 7 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistSuccessAsync_HtmlPage_HashMatchesStoredBytes()
    {
        // Arrange — use a small max so truncation happens; hash must match STORED bytes
        var (persister, repo, storage) = Build(new SnapshotOptions { MaxHtmlBytesPerPage = 20 });
        var scanId = ScanId.New();
        var html   = "<html>Content that is well over twenty bytes</html>";
        var fetch  = HtmlFetch("https://example.com/", html);

        // Act
        await persister.PersistSuccessAsync(scanId, fetch, crawlDepth: 0, CancellationToken.None);

        // Assert — hash in integrity record equals SHA-256 of what was actually stored
        var storedBlob    = storage.Saved.Values.Single();
        var expectedHash  = Sha256Hex(storedBlob);
        Assert.That(repo.Snapshots[0].Integrity.ContentHashSha256, Is.EqualTo(expectedHash));
    }

    // ── Test 8 ──────────────────────────────────────────────────────────────

    [Test]
    public async Task PersistFailureAsync_AnyReason_StoresFailedFetch()
    {
        // Arrange
        var (persister, repo, storage) = Build();
        var scanId     = ScanId.New();
        var url        = "https://example.com/broken";
        var failedAt   = DateTimeOffset.UtcNow;
        var reason     = FetchFailureReason.Timeout;
        var message    = "Connection timed out after 30 s.";

        // Act
        await persister.PersistFailureAsync(
            scanId, url, failedAt, crawlDepth: 2,
            reason, message, CancellationToken.None);

        // Assert — FailedFetch persisted; no HTML snapshot or blob
        Assert.That(repo.Failures, Has.Count.EqualTo(1));
        Assert.That(repo.Snapshots, Is.Empty);
        Assert.That(storage.Saved,  Is.Empty);

        var f = repo.Failures[0];
        Assert.That(f.Url,        Is.EqualTo(url));
        Assert.That(f.Reason,     Is.EqualTo(FetchFailureReason.Timeout));
        Assert.That(f.Message,    Is.EqualTo(message));
        Assert.That(f.CrawlDepth, Is.EqualTo(2));
        Assert.That(f.ScanId,     Is.EqualTo(scanId));
    }
}

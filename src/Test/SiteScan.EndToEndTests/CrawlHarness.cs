using SiteScan.Application.Crawling;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.Scanning;
using SiteScan.Infrastructure.Crawling;
using SiteScan.Infrastructure.Html;
using SiteScan.Infrastructure.Http;
using SiteScan.Infrastructure.Persistence;
using SiteScan.Infrastructure.Robots;

namespace SiteScan.EndToEndTests;

/// <summary>
/// Assembles the full crawler stack using real implementations wired to a
/// <see cref="FakeHttpHandler"/> so tests exercise every layer without making
/// real network requests.
/// </summary>
internal sealed class CrawlHarness
{
    private readonly FakeHttpHandler _handler = new();

    /// <summary>
    /// Default options suitable for fast tests: no politeness delay, short
    /// wall-clock timeout, everything else at production defaults.
    /// </summary>
    private CrawlerOptions _options = new()
    {
        MinDelayBetweenRequestsPerHost = TimeSpan.Zero,
        MaxWallClockTime = TimeSpan.FromSeconds(30),
    };

    // ── Configuration ───────────────────────────────────────────────────────

    /// <summary>Access the fake HTTP handler to register pages, resources, and robots.txt.</summary>
    public FakeHttpHandler Http => _handler;

    /// <summary>Override the default crawler options (e.g. to lower MaxDepth or MaxPagesPerScan).</summary>
    public CrawlHarness WithOptions(CrawlerOptions options)
    {
        _options = options;
        return this;
    }

    // ── Execution ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full crawl from <paramref name="startUrl"/> and returns every
    /// <see cref="CrawlRecord"/> written during the scan, ordered by timestamp.
    /// </summary>
    public async Task<IReadOnlyList<CrawlRecord>> RunAsync(
        string startUrl,
        CancellationToken ct = default)
    {
        // Single HttpClient backed by the fake handler; shared between
        // HttpFetcher and RobotsPolicy so both see the same fake site.
        var httpClient = new HttpClient(_handler, disposeHandler: false);

        var canonicalizer = new UrlCanonicalizer();
        var scope         = new ScopePolicy(_options, new SimpleRegistrableDomainResolver());
        var robots        = new RobotsPolicy(httpClient, _options);
        var politeness    = new PolitenessGate(_options);
        var fetcher       = new HttpFetcher(httpClient);
        var links         = new AngleSharpLinkExtractor();
        var store         = new InMemoryCrawlStore();

        var crawler = new Crawler(
            _options, canonicalizer, scope, robots,
            politeness, fetcher, links, store,
            NullSnapshotPersister.Instance);

        var scanId        = ScanId.New();
        var canonicalRoot = canonicalizer.CanonicalizeAbsolute(new Uri(startUrl));

        await crawler.RunAsync(scanId, canonicalRoot, ct);

        return await store.GetByScanIdAsync(scanId, ct);
    }
}

/// <summary>
/// Simple eTLD+1 resolver: returns the last two dot-separated labels of the host.
/// Sufficient for SameHost scope mode (which doesn't call this) and for basic
/// RegistrableDomain tests.
/// </summary>
internal sealed class SimpleRegistrableDomainResolver : IRegistrableDomainResolver
{
    public string? TryGetRegistrableDomain(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[^2..]) : host;
    }
}

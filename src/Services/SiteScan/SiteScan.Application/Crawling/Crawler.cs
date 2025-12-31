using SiteScan.Domain.Scanning;

namespace SiteScan.Application.Crawling;

public interface ICrawler
{
    Task RunAsync(ScanId scanId, Uri scanRootCanonical, CancellationToken ct);
}

public sealed class Crawler : ICrawler
{
    private readonly CrawlerOptions _options;
    private readonly IUrlCanonicalizer _canonicalizer;
    private readonly IScopePolicy _scope;
    private readonly IRobotsPolicy _robots;
    private readonly IPolitenessGate _politeness;
    private readonly IHttpFetcher _fetcher;
    private readonly IHtmlLinkExtractor _links;
    private readonly ICrawlRecordWriter _writer;

    public Crawler(
        CrawlerOptions options,
        IUrlCanonicalizer canonicalizer,
        IScopePolicy scope,
        IRobotsPolicy robots,
        IPolitenessGate politeness,
        IHttpFetcher fetcher,
        IHtmlLinkExtractor links,
        ICrawlRecordWriter writer)
    {
        _options = options;
        _canonicalizer = canonicalizer;
        _scope = scope;
        _robots = robots;
        _politeness = politeness;
        _fetcher = fetcher;
        _links = links;
        _writer = writer;
    }

    public async Task RunAsync(ScanId scanId, Uri scanRootCanonical, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var trapDetector = new TrapDetector(_options.MaxUrlLength, _options.MaxDistinctQueryCombosPerPath);

        var frontier = new CrawlFrontier();
        frontier.Enqueue(new FrontierItem(scanRootCanonical, 0));

        // canonical dedupe key -> fetched?
        var visited = new HashSet<string>(StringComparer.Ordinal);

        int fetchedCount = 0;

        while (!ct.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow - started > _options.MaxWallClockTime)
            {
                // stop crawl: max time
                await _writer.WriteAsync(new CrawlRecord(
                    scanId,
                    scanRootCanonical,
                    FinalUrl: null,
                    StatusCode: null,
                    ContentType: null,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Depth: 0,
                    Disposition: CrawlDisposition.Skipped,
                    SkipReason: SkipReason.LimitReached_MaxTime,
                    Notes: "Max wall-clock time reached; crawl stopped."
                ), ct);
                break;
            }

            if (fetchedCount >= _options.MaxPagesPerScan)
            {
                await _writer.WriteAsync(new CrawlRecord(
                    scanId,
                    scanRootCanonical,
                    FinalUrl: null,
                    StatusCode: null,
                    ContentType: null,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Depth: 0,
                    Disposition: CrawlDisposition.Skipped,
                    SkipReason: SkipReason.LimitReached_MaxPages,
                    Notes: $"Max pages reached: {_options.MaxPagesPerScan}."
                ), ct);
                break;
            }

            if (!frontier.TryDequeue(out var item))
            {
                if (frontier.IsEmpty) break;
                continue;
            }

            if (item.Depth > _options.MaxDepth)
            {
                await _writer.WriteAsync(new CrawlRecord(
                    scanId,
                    item.CanonicalUrl,
                    FinalUrl: null,
                    StatusCode: null,
                    ContentType: null,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Depth: item.Depth,
                    Disposition: CrawlDisposition.Skipped,
                    SkipReason: SkipReason.LimitReached_MaxDepth,
                    Notes: $"Max depth exceeded: {_options.MaxDepth}."
                ), ct);
                continue;
            }

            // Trap detection
            if (trapDetector.IsUrlTooLong(item.CanonicalUrl))
            {
                await _writer.WriteAsync(Skipped(scanId, item, SkipReason.TrapDetected_UrlTooLong, "URL length exceeds threshold."), ct);
                continue;
            }

            if (trapDetector.ExceedsQueryCombos(item.CanonicalUrl))
            {
                await _writer.WriteAsync(Skipped(scanId, item, SkipReason.TrapDetected_TooManyQueryCombos, "Too many distinct query combinations for path."), ct);
                continue;
            }

            // Scope check
            if (!_scope.IsInScope(item.CanonicalUrl, scanRootCanonical, out var scopeNotes))
            {
                await _writer.WriteAsync(Skipped(scanId, item, SkipReason.OutOfScope, scopeNotes), ct);
                continue;
            }

            // Dedupe check
            var key = _canonicalizer.GetDedupeKey(item.CanonicalUrl);
            if (!visited.Add(key))
            {
                await _writer.WriteAsync(Skipped(scanId, item, SkipReason.Duplicate, "Canonical URL already visited."), ct);
                continue;
            }

            // Robots check
            var robotsDecision = await _robots.CanFetchAsync(item.CanonicalUrl, ct);
            if (!robotsDecision.Allowed)
            {
                await _writer.WriteAsync(Skipped(scanId, item, SkipReason.RobotsDisallowed, robotsDecision.Notes), ct);
                continue;
            }

            // Politeness gate (per-host delay/concurrency)
            await _politeness.WaitTurnAsync(item.CanonicalUrl, ct);

            // Fetch
            FetchResult fetch;
            try
            {
                fetch = await _fetcher.FetchAsync(item.CanonicalUrl, ct);
            }
            catch (Exception ex)
            {
                // Still record as fetched attempt (status unknown); you may prefer SkipReason with Notes.
                await _writer.WriteAsync(new CrawlRecord(
                    scanId,
                    item.CanonicalUrl,
                    FinalUrl: null,
                    StatusCode: null,
                    ContentType: null,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Depth: item.Depth,
                    Disposition: CrawlDisposition.Skipped,
                    SkipReason: SkipReason.InvalidUrl,
                    Notes: $"Fetch failed: {ex.GetType().Name}: {ex.Message}"
                ), ct);
                continue;
            }

            fetchedCount++;

            // Evidence record for fetched page
            await _writer.WriteAsync(new CrawlRecord(
                scanId,
                item.CanonicalUrl,
                FinalUrl: fetch.FinalUrl,
                StatusCode: fetch.StatusCode,
                ContentType: fetch.ContentType,
                TimestampUtc: DateTimeOffset.UtcNow,
                Depth: item.Depth,
                Disposition: CrawlDisposition.Fetched,
                SkipReason: SkipReason.None,
                Notes: null
            ), ct);

            // Content handling: only HTML => link extraction
            if (!IsHtml(fetch.ContentType))
            {
                // optional: record that it was non-html, but already recorded as fetched
                // No link extraction
                continue;
            }

            var candidates = _links.ExtractLinks(fetch.Body);
            foreach (var href in candidates)
            {
                var canonicalCandidate = _canonicalizer.TryCanonicalize(fetch.FinalUrl, href);
                if (canonicalCandidate is null) continue;

                // normalize (already done), then enqueue (dedupe happens on dequeue)
                frontier.Enqueue(new FrontierItem(canonicalCandidate, item.Depth + 1));
            }
        }
    }

    private static bool IsHtml(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        // handle "text/html; charset=utf-8"
        return contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static CrawlRecord Skipped(ScanId scanId, FrontierItem item, SkipReason reason, string? notes)
        => new(
            scanId,
            item.CanonicalUrl,
            FinalUrl: null,
            StatusCode: null,
            ContentType: null,
            TimestampUtc: DateTimeOffset.UtcNow,
            Depth: item.Depth,
            Disposition: CrawlDisposition.Skipped,
            SkipReason: reason,
            Notes: notes
        );
}

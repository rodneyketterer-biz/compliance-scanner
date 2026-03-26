using NUnit.Framework;
using SiteScan.Application.Crawling;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.Scanning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SiteScan.UnitTests.Application;

internal class RecordingWriter : ICrawlRecordWriter
{
    public readonly List<CrawlRecord> Records = new();
    public Task WriteAsync(CrawlRecord record, CancellationToken ct)
    {
        Records.Add(record);
        return Task.CompletedTask;
    }
}

internal class AllowAllRobots : IRobotsPolicy
{
    private readonly RobotsDecision _decision;
    public AllowAllRobots(RobotsDecision decision) => _decision = decision;
    public Task<RobotsDecision> CanFetchAsync(Uri url, CancellationToken ct) => Task.FromResult(_decision);
}

internal class NoOpPoliteness : IPolitenessGate { public Task WaitTurnAsync(Uri url, CancellationToken ct) => Task.CompletedTask; }

internal class SimpleCanonicalizer : IUrlCanonicalizer
{
    public Uri? TryCanonicalize(Uri baseUri, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return CanonicalizeAbsolute(abs);
        if (Uri.TryCreate(baseUri, href, out var resolved)) return CanonicalizeAbsolute(resolved);
        return null;
    }

    public Uri CanonicalizeAbsolute(Uri absolute)
    {
        var b = new UriBuilder(absolute);
        b.Host = b.Host.ToLowerInvariant();
        b.Fragment = string.Empty;
        if ((b.Scheme == "http" && b.Port == 80) || (b.Scheme == "https" && b.Port == 443)) b.Port = -1;
        if (string.IsNullOrEmpty(b.Path)) b.Path = "/";
        return b.Uri;
    }

    public string GetDedupeKey(Uri canonical) => canonical.AbsoluteUri;
}

internal class SimpleScope : IScopePolicy
{
    public bool IsInScope(Uri candidateCanonical, Uri scanRootCanonical, out string? notes) { notes = null; return true; }
}

// No-op snapshot persister used by all tests in this file that exercise the
// crawl engine without needing real snapshot storage.
// NullSnapshotPersister.Instance (from Application) is the canonical version;
// this alias keeps the intent explicit at each call site.
internal static class NoopPersister
{
    public static ISnapshotPersister Instance => NullSnapshotPersister.Instance;
}

internal class FetcherReturning : IHttpFetcher
{
    private readonly FetchResult _result;
    public FetcherReturning(FetchResult result) => _result = result;
    public Task<FetchResult> FetchAsync(Uri url, CancellationToken ct) => Task.FromResult(_result);
}

internal class LinkExtractorReturning : IHtmlLinkExtractor
{
    public readonly List<string> Extracted = new();
    private readonly IReadOnlyList<string> _links;
    public LinkExtractorReturning(params string[] links) => _links = links;
    public IReadOnlyList<string> ExtractLinks(ReadOnlySpan<byte> htmlBytes)
    {
        Extracted.AddRange(_links);
        return _links;
    }
}

[TestFixture]
public class CrawlerTests
{
    [Test]
    public async Task RobotsDisallowed_Writes_Skipped_Robots()
    {
        var options = new CrawlerOptions();
        var canonical = new SimpleCanonicalizer();
        var scope = new SimpleScope();
        var robots = new AllowAllRobots(new RobotsDecision(false, "disallowed", Array.Empty<Uri>()));
        var politeness = new NoOpPoliteness();
        var writer = new RecordingWriter();

        // fetcher/links won't be used because robots disallow
        var fetcher = new FetcherReturning(new FetchResult(new Uri("https://example.com/"), new Uri("https://example.com/"), 200, "text/html", Array.Empty<byte>()));
        var links = new LinkExtractorReturning();

        var crawler = new Crawler(options, canonical, scope, robots, politeness, fetcher, links, writer, NoopPersister.Instance);

        await crawler.RunAsync(ScanId.New(), new Uri("https://example.com/"), CancellationToken.None);

        Assert.That(writer.Records.Any(r => r.Disposition == CrawlDisposition.Skipped && r.SkipReason == SkipReason.RobotsDisallowed), Is.True);
    }

    [Test]
    public async Task DuplicateCanonical_Produces_Skipped_Duplicate()
    {
        var options = new CrawlerOptions { MaxPagesPerScan = 10 };
        var canonical = new SimpleCanonicalizer();
        var scope = new SimpleScope();
        var robots = new AllowAllRobots(new RobotsDecision(true, null, Array.Empty<Uri>()));
        var politeness = new NoOpPoliteness();
        var writer = new RecordingWriter();

        // Fetch returns HTML and link extractor returns the same URL as the scan root -> duplicate
        var fetchResult = new FetchResult(new Uri("https://example.com/"), new Uri("https://example.com/"), 200, "text/html", System.Text.Encoding.UTF8.GetBytes("<a href=\"/\">root</a>"));
        var fetcher = new FetcherReturning(fetchResult);
        var links = new LinkExtractorReturning("/");

        var crawler = new Crawler(options, canonical, scope, robots, politeness, fetcher, links, writer, NoopPersister.Instance);

        await crawler.RunAsync(ScanId.New(), new Uri("https://example.com/"), CancellationToken.None);

        // Expect one fetched record and one duplicate skipped
        Assert.That(writer.Records.Any(r => r.Disposition == CrawlDisposition.Fetched), Is.True);
        Assert.That(writer.Records.Any(r => r.Disposition == CrawlDisposition.Skipped && r.SkipReason == SkipReason.Duplicate), Is.True);
    }

    [Test]
    public async Task NonHtml_DoesNot_Invoke_LinkExtractor_But_Records_Fetched()
    {
        var options = new CrawlerOptions();
        var canonical = new SimpleCanonicalizer();
        var scope = new SimpleScope();
        var robots = new AllowAllRobots(new RobotsDecision(true, null, Array.Empty<Uri>()));
        var politeness = new NoOpPoliteness();
        var writer = new RecordingWriter();

        var fetchResult = new FetchResult(new Uri("https://example.com/image.png"), new Uri("https://example.com/image.png"), 200, "image/png", Array.Empty<byte>());
        var fetcher = new FetcherReturning(fetchResult);

        var links = new LinkExtractorReturning("/should-not-be-called");

        var crawler = new Crawler(options, canonical, scope, robots, politeness, fetcher, links, writer, NoopPersister.Instance);

        await crawler.RunAsync(ScanId.New(), new Uri("https://example.com/image.png"), CancellationToken.None);

        Assert.That(writer.Records.Any(r => r.Disposition == CrawlDisposition.Fetched), Is.True);
        Assert.That(links.Extracted, Is.Empty);
    }
}

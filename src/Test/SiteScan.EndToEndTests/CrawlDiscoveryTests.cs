using NUnit.Framework;
using SiteScan.Application.Crawling;
using SiteScan.Domain.Scanning;

namespace SiteScan.EndToEndTests;

/// <summary>
/// End-to-end tests for page discovery and crawl behaviour.
///
/// Each test wires up the real crawler stack (Crawler, UrlCanonicalizer,
/// ScopePolicy, RobotsPolicy, PolitenessGate, HttpFetcher,
/// AngleSharpLinkExtractor, InMemoryCrawlStore) backed by a
/// <see cref="FakeHttpHandler"/> so no real network traffic is generated.
/// </summary>
[TestFixture]
public sealed class CrawlDiscoveryTests
{
    // ── 1. Single page, no outbound links ───────────────────────────────────

    [Test]
    public async Task SinglePage_WithNoLinks_RecordsSingleFetch()
    {
        // Arrange
        var harness = new CrawlHarness();
        harness.Http.AddPage(
            "http://example.com/",
            "<html><body>Hello World</body></html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert
        var fetched = records.Where(r => r.Disposition == CrawlDisposition.Fetched).ToList();
        Assert.That(fetched, Has.Count.EqualTo(1));
        Assert.That(fetched[0].Url.AbsoluteUri, Is.EqualTo("http://example.com/"));
    }

    // ── 2. Root with internal links discovers all linked pages ───────────────

    [Test]
    public async Task RootWithInternalLinks_DiscoversLinkedPages()
    {
        // Arrange
        var harness = new CrawlHarness();
        harness.Http.AddPage(
            "http://example.com/",
            "<html><body><a href=\"/about\">About</a><a href=\"/contact\">Contact</a></body></html>");
        harness.Http.AddPage("http://example.com/about",   "<html><body>About</body></html>");
        harness.Http.AddPage("http://example.com/contact", "<html><body>Contact</body></html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert
        var fetched = records
            .Where(r => r.Disposition == CrawlDisposition.Fetched)
            .Select(r => r.Url.AbsoluteUri)
            .ToHashSet();

        Assert.That(fetched, Is.EquivalentTo(new[]
        {
            "http://example.com/",
            "http://example.com/about",
            "http://example.com/contact"
        }));
    }

    // ── 3. Out-of-scope links are not followed ───────────────────────────────

    [Test]
    public async Task OutOfScopeLink_IsSkipped()
    {
        // Arrange
        var harness = new CrawlHarness();
        harness.Http.AddPage(
            "http://example.com/",
            "<html><body><a href=\"http://other.com/page\">External</a></body></html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert — root is fetched; external link is skipped as out-of-scope
        Assert.That(
            records.Count(r => r.Disposition == CrawlDisposition.Fetched),
            Is.EqualTo(1));

        Assert.That(
            records.Any(r =>
                r.Disposition == CrawlDisposition.Skipped &&
                r.SkipReason == SkipReason.OutOfScope),
            Is.True);
    }

    // ── 4. Pages beyond MaxDepth are skipped ────────────────────────────────

    [Test]
    public async Task MaxDepth_ExceededPages_AreSkipped()
    {
        // Arrange — chain: / (depth 0) → /level1 (depth 1) → /level2 (depth 2)
        // With MaxDepth=1, /level2 must be skipped.
        var harness = new CrawlHarness();
        harness.WithOptions(new CrawlerOptions
        {
            MinDelayBetweenRequestsPerHost = TimeSpan.Zero,
            MaxDepth = 1,
        });

        harness.Http.AddPage("http://example.com/",       "<html><a href=\"/level1\">L1</a></html>");
        harness.Http.AddPage("http://example.com/level1", "<html><a href=\"/level2\">L2</a></html>");
        harness.Http.AddPage("http://example.com/level2", "<html>Level 2</html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert
        var fetched = records
            .Where(r => r.Disposition == CrawlDisposition.Fetched)
            .Select(r => r.Url.AbsoluteUri)
            .ToList();

        Assert.That(fetched, Is.EquivalentTo(new[]
        {
            "http://example.com/",
            "http://example.com/level1"
        }));

        Assert.That(
            records.Any(r =>
                r.Disposition == CrawlDisposition.Skipped &&
                r.SkipReason == SkipReason.LimitReached_MaxDepth &&
                r.Url.AbsolutePath == "/level2"),
            Is.True);
    }

    // ── 5. Duplicate URLs are fetched only once ──────────────────────────────

    [Test]
    public async Task DuplicateUrl_IsFetchedOnlyOnce()
    {
        // Arrange — root links to /page-a and /shared; /page-a also links to /shared
        var harness = new CrawlHarness();
        harness.Http.AddPage(
            "http://example.com/",
            "<html><a href=\"/page-a\">A</a><a href=\"/shared\">S</a></html>");
        harness.Http.AddPage(
            "http://example.com/page-a",
            "<html><a href=\"/shared\">S again</a></html>");
        harness.Http.AddPage("http://example.com/shared", "<html>Shared</html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert — /shared is fetched exactly once; the second encounter is a duplicate
        var fetchedUrls = records
            .Where(r => r.Disposition == CrawlDisposition.Fetched)
            .Select(r => r.Url.AbsoluteUri)
            .ToList();

        Assert.That(fetchedUrls.Count(u => u == "http://example.com/shared"), Is.EqualTo(1));

        Assert.That(
            records.Any(r =>
                r.Disposition == CrawlDisposition.Skipped &&
                r.SkipReason == SkipReason.Duplicate),
            Is.True);
    }

    // ── 6. Robots-disallowed paths are not fetched ───────────────────────────

    [Test]
    public async Task RobotsDisallowed_PathIsSkipped()
    {
        // Arrange — robots.txt disallows /admin/
        var harness = new CrawlHarness();
        harness.Http.AddRobots("http://example.com/", """
            User-agent: *
            Disallow: /admin/
            """);

        harness.Http.AddPage(
            "http://example.com/",
            "<html><a href=\"/admin/dashboard\">Admin</a></html>");

        harness.Http.AddPage("http://example.com/admin/dashboard", "<html>Dashboard</html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert — root is fetched; /admin/dashboard is blocked by robots
        Assert.That(
            records.Count(r => r.Disposition == CrawlDisposition.Fetched),
            Is.EqualTo(1));

        Assert.That(
            records.Any(r =>
                r.Disposition == CrawlDisposition.Skipped &&
                r.SkipReason == SkipReason.RobotsDisallowed),
            Is.True);
    }

    // ── 7. MaxPagesPerScan stops the crawl ───────────────────────────────────

    [Test]
    public async Task MaxPagesPerScan_StopsCrawlAfterLimit()
    {
        // Arrange — root links to three subpages; cap at 2 fetches
        var harness = new CrawlHarness();
        harness.WithOptions(new CrawlerOptions
        {
            MinDelayBetweenRequestsPerHost = TimeSpan.Zero,
            MaxPagesPerScan = 2,
        });

        harness.Http.AddPage(
            "http://example.com/",
            "<html><a href=\"/a\">A</a><a href=\"/b\">B</a><a href=\"/c\">C</a></html>");
        harness.Http.AddPage("http://example.com/a", "<html>A</html>");
        harness.Http.AddPage("http://example.com/b", "<html>B</html>");
        harness.Http.AddPage("http://example.com/c", "<html>C</html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert — exactly 2 pages fetched; crawl halted with MaxPages reason
        Assert.That(
            records.Count(r => r.Disposition == CrawlDisposition.Fetched),
            Is.EqualTo(2));

        Assert.That(
            records.Any(r =>
                r.Disposition == CrawlDisposition.Skipped &&
                r.SkipReason == SkipReason.LimitReached_MaxPages),
            Is.True);
    }

    // ── 8. Non-HTML resources are fetched but not link-extracted ─────────────

    [Test]
    public async Task NonHtmlContent_IsFetchedWithoutFollowingEmbeddedUrls()
    {
        // Arrange — root links only to a CSS file via <link href>.
        // The CSS body contains text that looks like a path but is never extracted
        // because link extraction is skipped for non-HTML content types.
        var harness = new CrawlHarness();
        harness.Http.AddPage(
            "http://example.com/",
            "<html><head><link href=\"/style.css\" rel=\"stylesheet\"></head><body></body></html>");
        harness.Http.AddResource(
            "http://example.com/style.css",
            "text/css",
            "body { background: url('/hidden-page'); }");

        // /hidden-page is registered but must never be fetched
        harness.Http.AddPage("http://example.com/hidden-page", "<html>Hidden</html>");

        // Act
        var records = await harness.RunAsync("http://example.com/");

        // Assert — root and CSS are fetched; /hidden-page is never discovered
        var fetchedUrls = records
            .Where(r => r.Disposition == CrawlDisposition.Fetched)
            .Select(r => r.Url.AbsoluteUri)
            .ToList();

        Assert.That(fetchedUrls, Is.EquivalentTo(new[]
        {
            "http://example.com/",
            "http://example.com/style.css"
        }));

        Assert.That(
            fetchedUrls.Contains("http://example.com/hidden-page"),
            Is.False,
            "Hidden page must not be discovered from non-HTML content.");
    }
}

using System.Text;
using AngleSharp;
using SiteScan.Application.Crawling;

namespace SiteScan.Infrastructure.Html;

public sealed class AngleSharpLinkExtractor : IHtmlLinkExtractor
{
    private readonly IBrowsingContext _ctx;

    public AngleSharpLinkExtractor()
    {
        var config = Configuration.Default;
        _ctx = BrowsingContext.New(config);
    }

    public IReadOnlyList<string> ExtractLinks(ReadOnlySpan<byte> htmlBytes)
    {
        // best-effort decode; you can improve by honoring charset from Content-Type
        var html = Encoding.UTF8.GetString(htmlBytes);
        var doc = _ctx.OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

        var results = new List<string>(capacity: 256);

        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href)) results.Add(href);
        }

        // optional: also crawl <link href>, <script src>, <img src> for discovery
        foreach (var link in doc.QuerySelectorAll("link[href]"))
        {
            var href = link.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href)) results.Add(href);
        }

        return results;
    }
}

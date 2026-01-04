using NUnit.Framework;
using SiteScan.Application.Crawling;
using System;

namespace SiteScan.UnitTests.Application;

[TestFixture]
public class UrlCanonicalizerTests
{
    [Test]
    public void TryCanonicalize_Ignores_NonHttp_Schemes()
    {
        var c = new UrlCanonicalizer();
        var baseUri = new Uri("https://example.com/");

        Assert.Multiple(() =>
        {
            Assert.That(c.TryCanonicalize(baseUri, "mailto:user@example.com"), Is.Null);
            Assert.That(c.TryCanonicalize(baseUri, "javascript:alert(1)"), Is.Null);
            Assert.That(c.TryCanonicalize(baseUri, "tel:+123"), Is.Null);
            Assert.That(c.TryCanonicalize(baseUri, "data:text/plain,hello"), Is.Null);
        });
    }

    [Test]
    public void CanonicalizeAbsolute_Normalizes_Host_Fragment_DefaultPort_And_Collapses_Slashes()
    {
        var c = new UrlCanonicalizer();
        var input = new Uri("HTTP://ExAmPlE.COM:80//a///b#frag");

        var result = c.CanonicalizeAbsolute(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Scheme, Is.EqualTo("http"));
            Assert.That(result.Host, Is.EqualTo("example.com"));
            Assert.That(result.Fragment, Is.Empty);
            Assert.That(result, Is.EqualTo(new Uri("http://example.com/a/b")));
            Assert.That(c.GetDedupeKey(result), Is.EqualTo("http://example.com/a/b"));
        });
    }

    [Test]
    public void TryCanonicalize_Resolves_Relative_Href()
    {
        var c = new UrlCanonicalizer();
        var baseUri = new Uri("https://example.com/dir/page.html");

        var resolved = c.TryCanonicalize(baseUri, "../other.html#x");

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved, Is.EqualTo(new Uri("https://example.com/other.html")));
    }
}

using NUnit.Framework;
using SiteScan.Application.Crawling;
using System;

namespace SiteScan.UnitTests.Application;

[TestFixture]
public class CollapseDuplicateSlashesTests
{
    [TestCase("http://example.com//a///b", "/a/b")]
    [TestCase("http://example.com/", "/")]
    [TestCase("http://example.com///", "/")]
    [TestCase("http://example.com/a//b//c", "/a/b/c")]
    [TestCase("http://example.com/a", "/a")]
    public void CanonicalizeAbsolute_Collapses_Duplicate_Slashes(string input, string expectedPath)
    {
        var c = new UrlCanonicalizer();
        var uri = new Uri(input);

        var can = c.CanonicalizeAbsolute(uri);

        Assert.That(can.AbsolutePath, Is.EqualTo(expectedPath));
    }

    [Test]
    public void CanonicalizeAbsolute_Preserves_Path_Casing_But_Lowercases_Host()
    {
        var c = new UrlCanonicalizer();
        var uri = new Uri("http://EXAMPLE.com/Some/Path");

        var can = c.CanonicalizeAbsolute(uri);

        Assert.That(can.Host, Is.EqualTo("example.com"));
        Assert.That(can.AbsolutePath, Is.EqualTo("/Some/Path"));
    }
}

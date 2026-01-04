using NUnit.Framework;
using SiteScan.Application.Crawling;
using System;

namespace SiteScan.UnitTests.Application;

[TestFixture]
public class TrapDetectorTests
{
    [Test]
    public void IsUrlTooLong_ReturnsTrue_When_ExceedsThreshold()
    {
        var d = new TrapDetector(maxUrlLength: 15, maxQueryCombosPerPath: 5);
        var shortUri = new Uri("https://a.");
        var longUri = new Uri("https://example.com/this/is/a/very/long/path");

        Assert.Multiple(() =>
        {
            Assert.That(d.IsUrlTooLong(shortUri), Is.False);
            Assert.That(d.IsUrlTooLong(longUri), Is.True);
        });
    }

    [Test]
    public void ExceedsQueryCombos_BecomesTrue_After_Limit()
    {
        var d = new TrapDetector(maxUrlLength: 2000, maxQueryCombosPerPath: 2);
        var base1 = "https://example.com/path";

        var u1 = new Uri(base1 + "?a=1");
        var u2 = new Uri(base1 + "?b=1");
        var u3 = new Uri(base1 + "?c=1");

        Assert.That(d.ExceedsQueryCombos(u1), Is.False); // count = 1
        Assert.That(d.ExceedsQueryCombos(u2), Is.False); // count = 2
        Assert.That(d.ExceedsQueryCombos(u3), Is.True);  // count = 3 -> exceeds 2
    }

    [Test]
    public void ExceedsQueryCombos_IsScoped_By_Path()
    {
        var d = new TrapDetector(maxUrlLength: 2000, maxQueryCombosPerPath: 1);
        var a1 = new Uri("https://example.com/path?x=1");
        var a2 = new Uri("https://example.com/path?y=1");
        var b1 = new Uri("https://example.com/other?x=1");

        Assert.That(d.ExceedsQueryCombos(a1), Is.False);
        Assert.That(d.ExceedsQueryCombos(a2), Is.True); // same path, second distinct combo -> exceeds
        Assert.That(d.ExceedsQueryCombos(b1), Is.False); // different path has its own counter
    }
}

using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SiteScan.Domain.Scanning;

namespace SiteScan.UnitTests.Domain;

[TestFixture]
public class ScanIdTests
{
    [Test]
    public void New_Returns32CharHexString()
    {
        var id = ScanId.New();
        var s = id.ToString();
        Assert.That(s.Length, Is.EqualTo(32));
        Assert.That(Hex32Regex().IsMatch(s), Is.True);
    }

    [Test]
    public void ToString_MatchesGuidNFormat()
    {
        var g = Guid.NewGuid();
        var id = new ScanId(g);
        Assert.That(id.ToString(), Is.EqualTo(g.ToString("N")));
    }

    [Test]
    public void Equality_And_HashCode_Work_ForSameGuid()
    {
        var g = Guid.NewGuid();
        var a = new ScanId(g);
        var b = new ScanId(g);
        Assert.That(b, Is.EqualTo(a));
        Assert.That(b.GetHashCode(), Is.EqualTo(a.GetHashCode()));
    }

    [Test]
    public void New_GeneratesDifferentValues_Usually()
    {
        var a = ScanId.New();
        var b = ScanId.New();
        Assert.That(a, Is.Not.EqualTo(b));
    }

    private static Regex Hex32Regex() => new Regex("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);
}

using System;
using NUnit.Framework;
using SiteScan.Domain.Scanning;

namespace SiteScan.UnitTests.Domain;

[TestFixture]
public class CrawlEnumsTests
{
    [Test]
    public void CrawlDisposition_Values_AreStable()
    {
        Assert.That((int)CrawlDisposition.Fetched, Is.EqualTo(0));
        Assert.That((int)CrawlDisposition.Skipped, Is.EqualTo(1));
    }

    [Test]
    public void SkipReason_KeyValues_AreStable()
    {
        Assert.That((int)SkipReason.None, Is.EqualTo(0));
        Assert.That((int)SkipReason.OutOfScope, Is.EqualTo(1));
        Assert.That((int)SkipReason.RobotsDisallowed, Is.EqualTo(2));
        Assert.That((int)SkipReason.Duplicate, Is.EqualTo(3));
        Assert.That((int)SkipReason.InvalidUrl, Is.EqualTo(9));
        Assert.That((int)SkipReason.NonHtml_NoLinkExtraction, Is.EqualTo(10));
    }
}

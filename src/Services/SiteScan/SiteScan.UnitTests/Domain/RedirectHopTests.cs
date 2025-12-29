using System;
using NUnit.Framework;
using SiteScan.Domain.UrlResolution;

namespace SiteScan.UnitTests.Domain;

[TestFixture]
public class RedirectHopTests
{
    [Test]
    public void RedirectHop_PreservesValues()
    {
        var uri = new Uri("http://example.com/");
        var hop = new RedirectHop(uri, 301, "http://example.com/next");
        Assert.That(hop.Url, Is.EqualTo(uri));
        Assert.That(hop.StatusCode, Is.EqualTo(301));
        Assert.That(hop.LocationHeader, Is.EqualTo("http://example.com/next"));
    }
}

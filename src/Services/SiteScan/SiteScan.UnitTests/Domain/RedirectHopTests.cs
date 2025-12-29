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
        Assert.AreEqual(uri, hop.Url);
        Assert.AreEqual(301, hop.StatusCode);
        Assert.AreEqual("http://example.com/next", hop.LocationHeader);
    }
}

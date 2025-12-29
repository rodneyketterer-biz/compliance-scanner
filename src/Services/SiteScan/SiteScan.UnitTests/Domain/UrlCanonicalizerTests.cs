using System;
using NUnit.Framework;
using SiteScan.Domain.UrlResolution;

namespace SiteScan.UnitTests.Domain;

[TestFixture]
public class UrlCanonicalizerTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void HostLowercased_FragmentRemoved_DefaultPortAndPathHandled()
    {
        var input = "HTTP://Example.COM:80/path#frag";
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput(input);
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("http://example.com/path", outcome.CanonicalUri!.ToString().TrimEnd('/'));
    }

    [Test]
    public void EmptyPathBecomesSlash_And_DefaultPortRemoved()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com");
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("http://example.com/", outcome.CanonicalUri!.ToString());
    }

    [Test]
    public void CollapseDuplicateSlashesInPath()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com//a///b");
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("/a/b", outcome.CanonicalUri!.AbsolutePath);
    }

    [Test]
    public void QueryPreservedVerbatim()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com/path?b=2&a=1#x");
        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("?b=2&a=1", outcome.CanonicalUri!.Query);
    }

    [Test]
    public void MissingSchemeIsDetected()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("example.com/path");
        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(UrlErrorCode.MissingScheme, outcome.Error!.Code);
    }
}

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
        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.CanonicalUri!.ToString().TrimEnd('/'), Is.EqualTo("http://example.com/path"));
    }

    [Test]
    public void EmptyPathBecomesSlash_And_DefaultPortRemoved()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com");
        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.CanonicalUri!.ToString(), Is.EqualTo("http://example.com/"));
    }

    [Test]
    public void CollapseDuplicateSlashesInPath()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com//a///b");
        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.CanonicalUri!.AbsolutePath, Is.EqualTo("/a/b"));
    }

    [Test]
    public void QueryPreservedVerbatim()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("http://example.com/path?b=2&a=1#x");
        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.CanonicalUri!.Query, Is.EqualTo("?b=2&a=1"));
    }

    [Test]
    public void MissingSchemeIsDetected()
    {
        var outcome = UrlCanonicalizer.CanonicalizeFromUserInput("example.com/path");
        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Error!.Code, Is.EqualTo(UrlErrorCode.MissingScheme));
    }
}

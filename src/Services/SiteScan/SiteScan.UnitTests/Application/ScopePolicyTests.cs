using NUnit.Framework;
using SiteScan.Application.Crawling;
using System;

namespace SiteScan.UnitTests.Application;

class FakeRegistrableResolver : IRegistrableDomainResolver
{
    private readonly Func<string?, string?> _fn;
    public FakeRegistrableResolver(Func<string?, string?> fn) => _fn = fn;
    public string? TryGetRegistrableDomain(string host) => _fn(host);
}

[TestFixture]
public class ScopePolicyTests
{
    [Test]
    public void SameHost_Allows_SameHost_Port_And_Scheme()
    {
        var options = new CrawlerOptions { ScopeMode = ScopeMode.SameHost };
        var policy = new ScopePolicy(options, new FakeRegistrableResolver(h => null));

        var candidate = new Uri("https://example.com/some");
        var root = new Uri("https://example.com/");

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsInScope(candidate, root, out var notes), Is.True);
            Assert.That(notes, Is.Null);
        });
    }

    [Test]
    public void SameHost_Rejects_Different_Host_Or_Scheme()
    {
        var options = new CrawlerOptions { ScopeMode = ScopeMode.SameHost };
        var policy = new ScopePolicy(options, new FakeRegistrableResolver(h => null));

        var candidate = new Uri("http://example.com/"); // different scheme
        var root = new Uri("https://example.com/");

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsInScope(candidate, root, out var notes), Is.False);
            Assert.That(notes, Is.Not.Null);
        });
    }

    [Test]
    public void RegistrableDomain_Allows_When_RegistrableDomainsMatch()
    {
        var options = new CrawlerOptions { ScopeMode = ScopeMode.RegistrableDomain };
        var resolver = new FakeRegistrableResolver(h => h is not null && h.EndsWith("example.co.uk") ? "example.co.uk" : null);
        var policy = new ScopePolicy(options, resolver);

        var candidate = new Uri("https://sub.example.co.uk/path");
        var root = new Uri("https://example.co.uk/");

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsInScope(candidate, root, out var notes), Is.True);
            Assert.That(notes, Is.Null);
        });
    }

    [Test]
    public void RegistrableDomain_Rejects_When_Resolver_Returns_Null()
    {
        var options = new CrawlerOptions { ScopeMode = ScopeMode.RegistrableDomain };
        var resolver = new FakeRegistrableResolver(h => null);
        var policy = new ScopePolicy(options, resolver);

        var candidate = new Uri("https://unknownhost/whatever");
        var root = new Uri("https://example.com/");

        Assert.Multiple(() =>
        {
            Assert.That(policy.IsInScope(candidate, root, out var notes), Is.False);
            Assert.That(notes, Is.Not.Null);
        });
    }
}

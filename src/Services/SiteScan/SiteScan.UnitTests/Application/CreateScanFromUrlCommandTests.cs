using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SiteScan.Application.Scans;
using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;

namespace SiteScan.UnitTests.Application;

public class FakeResolver : IUrlResolver
{
    private readonly UrlResolutionResult _result;
    public FakeResolver(UrlResolutionResult result) => _result = result;
    public Task<UrlResolutionResult> ValidateNormalizeAndResolveAsync(string userSubmittedUrl, UrlResolutionOptions options, CancellationToken ct)
        => Task.FromResult(_result);
}

[TestFixture]
public class CreateScanFromUrlCommandTests
{
    [Test]
    public async Task Handler_PopulatesScanRecord_FromResolverResult()
    {
        var result = new UrlResolutionResult
        {
            OriginalSubmittedUrl = "http://input.example/",
            CanonicalScanRootUrl = new Uri("http://example.com/"),
            RedirectChain = new List<RedirectHop>()
        };

        var handler = new CreateScanFromUrlHandler(new FakeResolver(result), new UrlResolutionOptions());
        var scan = await handler.Handle(new CreateScanFromUrlCommand("http://input.example/"), CancellationToken.None);

        Assert.That(scan.OriginalSubmittedUrl, Is.EqualTo(result.OriginalSubmittedUrl));
        Assert.That(scan.CanonicalScanRootUrl, Is.EqualTo(result.CanonicalScanRootUrl));
        Assert.That(scan.RedirectChain, Is.SameAs(result.RedirectChain));
        Assert.That(scan.ErrorCode, Is.Null);
    }
}

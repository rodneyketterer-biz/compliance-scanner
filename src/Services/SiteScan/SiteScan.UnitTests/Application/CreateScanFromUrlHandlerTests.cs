using NUnit.Framework;
using SiteScan.Application.Abstractions;
using SiteScan.Application.Scans;
using SiteScan.Domain.UrlResolution;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SiteScan.UnitTests.Application;

class FakeUrlResolver : IUrlResolver
{
    private readonly UrlResolutionResult _result;
    public FakeUrlResolver(UrlResolutionResult result) => _result = result;

    public Task<UrlResolutionResult> ValidateNormalizeAndResolveAsync(string userSubmittedUrl, UrlResolutionOptions options, CancellationToken ct)
        => Task.FromResult(_result);
}

[TestFixture]
public class CreateScanFromUrlHandlerTests
{
    [Test]
    public async Task Handle_Maps_Successful_Resolution_To_ScanRecord()
    {
        var result = new UrlResolutionResult
        {
            OriginalSubmittedUrl = "http://in/",
            CanonicalScanRootUrl = new Uri("https://example.com/"),
            RedirectChain = new List<RedirectHop>
            {
                new(new Uri("http://in/"), 0, "https://example.com/")
            }
        };

        var resolver = new FakeUrlResolver(result);
        var handler = new CreateScanFromUrlHandler(resolver, new UrlResolutionOptions());

        var record = await handler.Handle(new CreateScanFromUrlCommand("http://in/"), CancellationToken.None);

        Assert.That(record.OriginalSubmittedUrl, Is.EqualTo(result.OriginalSubmittedUrl));
        Assert.That(record.CanonicalScanRootUrl, Is.EqualTo(result.CanonicalScanRootUrl));
        Assert.That(record.RedirectChain, Is.EqualTo(result.RedirectChain));
        Assert.That(record.ErrorCode, Is.Null);
        Assert.That(record.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task Handle_Maps_Error_To_ScanRecord_ErrorFields()
    {
        var err = new UrlResolutionError(UrlErrorCode.InvalidAbsoluteUri, "bad url");
        var result = new UrlResolutionResult
        {
            OriginalSubmittedUrl = "not-a-url",
            CanonicalScanRootUrl = null,
            RedirectChain = new List<RedirectHop>(),
            Error = err
        };

        var resolver = new FakeUrlResolver(result);
        var handler = new CreateScanFromUrlHandler(resolver, new UrlResolutionOptions());

        var record = await handler.Handle(new CreateScanFromUrlCommand("not-a-url"), CancellationToken.None);

        Assert.That(record.OriginalSubmittedUrl, Is.EqualTo(result.OriginalSubmittedUrl));
        Assert.That(record.CanonicalScanRootUrl, Is.Null);
        Assert.That(record.RedirectChain, Is.EqualTo(result.RedirectChain));
        Assert.That(record.ErrorCode, Is.EqualTo(err.Code));
        Assert.That(record.ErrorMessage, Is.EqualTo(err.Message));
    }
}

using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;

namespace SiteScan.Application.Scans;

// Example DTO representing what you persist on a "scan record"
public sealed record ScanRecord
{
    public required Guid Id { get; init; }
    public required string OriginalSubmittedUrl { get; init; }
    public Uri? CanonicalScanRootUrl { get; init; }
    public required IReadOnlyList<RedirectHop> RedirectChain { get; init; }
    public UrlErrorCode? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

// Command/handler style (MediatR-friendly, but no dependency required)
public sealed record CreateScanFromUrlCommand(string Url);

public sealed class CreateScanFromUrlHandler
{
    private readonly IUrlResolver _urlResolver;
    private readonly UrlResolutionOptions _options;

    public CreateScanFromUrlHandler(IUrlResolver urlResolver, UrlResolutionOptions options)
    {
        _urlResolver = urlResolver;
        _options = options;
    }

    public async Task<ScanRecord> Handle(CreateScanFromUrlCommand cmd, CancellationToken ct)
    {
        var result = await _urlResolver.ValidateNormalizeAndResolveAsync(cmd.Url, _options, ct);

        // Persistence: store original (verbatim), canonical (final resolved), redirect chain, plus error if failing.
        return new ScanRecord
        {
            Id = Guid.NewGuid(),
            OriginalSubmittedUrl = result.OriginalSubmittedUrl,
            CanonicalScanRootUrl = result.CanonicalScanRootUrl,
            RedirectChain = result.RedirectChain,
            ErrorCode = result.Error?.Code,
            ErrorMessage = result.Error?.Message
        };
    }
}

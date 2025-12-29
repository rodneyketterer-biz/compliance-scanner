namespace SiteScan.Domain.UrlResolution;

public sealed record UrlResolutionResult
{
    public required string OriginalSubmittedUrl { get; init; } // verbatim
    public Uri? CanonicalScanRootUrl { get; init; }           // final resolved + normalized
    public required IReadOnlyList<RedirectHop> RedirectChain { get; init; }

    public bool Success => Error is null;
    public UrlResolutionError? Error { get; init; }
}

public sealed record UrlResolutionError(
    UrlErrorCode Code,
    string Message
);

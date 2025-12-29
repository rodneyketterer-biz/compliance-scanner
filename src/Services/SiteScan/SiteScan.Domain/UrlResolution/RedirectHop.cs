namespace SiteScan.Domain.UrlResolution;

public sealed record RedirectHop(
    Uri Url,
    int StatusCode,
    string? LocationHeader
);
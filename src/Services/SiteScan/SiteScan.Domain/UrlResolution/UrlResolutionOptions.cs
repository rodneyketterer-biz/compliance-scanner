namespace SiteScan.Domain.UrlResolution;

public sealed record UrlResolutionOptions
{
    public int MaxRedirects { get; init; } = 10;

    // Transport
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromSeconds(20);

    // Final status allowlist
    public ISet<int> AllowedFinalStatusCodes { get; init; } = new HashSet<int> { 200, 301, 302 };
}

using SiteScan.Domain.UrlResolution;

namespace SiteScan.Application.Abstractions;

public interface IUrlResolver
{
    Task<UrlResolutionResult> ValidateNormalizeAndResolveAsync(
        string userSubmittedUrl,
        UrlResolutionOptions options,
        CancellationToken ct);
}

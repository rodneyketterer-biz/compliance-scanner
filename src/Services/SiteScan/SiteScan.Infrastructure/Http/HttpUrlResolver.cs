using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;

namespace SiteScan.Infrastructure.Http;

public sealed class HttpUrlResolver : IUrlResolver
{
    private static readonly ISet<int> RedirectStatusCodes = new HashSet<int> { 301, 302, 303, 307, 308 };

    private readonly IHttpClientFactory _httpClientFactory;

    public HttpUrlResolver(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UrlResolutionResult> ValidateNormalizeAndResolveAsync(
        string userSubmittedUrl,
        UrlResolutionOptions options,
        CancellationToken ct)
    {
        // 1) Validate + normalize initial URL from user input
        var initialOutcome = UrlCanonicalizer.CanonicalizeFromUserInput(userSubmittedUrl);
        if (!initialOutcome.Success)
        {
            return Fail(userSubmittedUrl, initialOutcome.Error!, Array.Empty<RedirectHop>());
        }

        var current = initialOutcome.CanonicalUri!;
        var chain = new List<RedirectHop>(capacity: Math.Max(2, options.MaxRedirects));

        // Dedicated cancellation for overall timeout (separate from connect timeout)
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        overallCts.CancelAfter(options.OverallTimeout);

        // Use named client configured with connect timeout and redirect disabled
        var client = _httpClientFactory.CreateClient(HttpClientNames.UrlResolution);

        try
        {
            for (var i = 0; i <= options.MaxRedirects; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, current);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SiteScan", "1.0"));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, overallCts.Token);

                var statusCode = (int)resp.StatusCode;
                var location = resp.Headers.Location?.ToString();

                chain.Add(new RedirectHop(current, statusCode, location));

                // Redirect?
                if (RedirectStatusCodes.Contains(statusCode))
                {
                    if (i == options.MaxRedirects)
                    {
                        return Fail(userSubmittedUrl,
                            new UrlResolutionError(UrlErrorCode.RedirectLimitExceeded,
                                $"Redirect limit exceeded (max {options.MaxRedirects})."),
                            chain);
                    }

                    if (resp.Headers.Location is null)
                    {
                        return Fail(userSubmittedUrl,
                            new UrlResolutionError(UrlErrorCode.RedirectMissingLocation,
                                $"Redirect response ({statusCode}) missing Location header."),
                            chain);
                    }

                    var nextRaw = ResolveLocation(current, resp.Headers.Location);

                    // Validate scheme and canonicalize the redirect target (query may change due to redirect)
                    var nextOutcome = UrlCanonicalizer.CanonicalizeUri(nextRaw, originalText: nextRaw.OriginalString);
                    if (!nextOutcome.Success)
                    {
                        // Specifically map unsupported scheme during redirects
                        var code = nextOutcome.Error!.Code == UrlErrorCode.UnsupportedScheme
                            ? UrlErrorCode.RedirectUnsupportedScheme
                            : nextOutcome.Error!.Code;

                        return Fail(userSubmittedUrl,
                            new UrlResolutionError(code, nextOutcome.Error!.Message),
                            chain);
                    }

                    current = nextOutcome.CanonicalUri!;
                    continue;
                }

                // 2) Final status allowlist gate
                if (!options.AllowedFinalStatusCodes.Contains(statusCode))
                {
                    return Fail(userSubmittedUrl,
                        new UrlResolutionError(
                            UrlErrorCode.FinalStatusNotAllowed,
                            $"URL is reachable but rejected due to status code. Final URL: {current} (HTTP {statusCode})."),
                        chain,
                        canonicalScanRoot: current);
                }

                // Success: final URL is the canonical scan root URL
                return new UrlResolutionResult
                {
                    OriginalSubmittedUrl = userSubmittedUrl,
                    CanonicalScanRootUrl = current,
                    RedirectChain = chain
                };
            }

            // Should be unreachable; loop returns in all paths
            return Fail(userSubmittedUrl,
                new UrlResolutionError(UrlErrorCode.RedirectLimitExceeded, "Redirect limit exceeded."),
                chain);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // overall timeout
            return Fail(userSubmittedUrl,
                new UrlResolutionError(UrlErrorCode.RequestTimeout,
                    $"Request timed out after {options.OverallTimeout.TotalSeconds:0.#}s."),
                chain,
                canonicalScanRoot: current);
        }
        catch (HttpRequestException ex)
        {
            var mapped = MapHttpException(ex, options);
            return Fail(userSubmittedUrl, mapped, chain, canonicalScanRoot: current);
        }
    }

    private static Uri ResolveLocation(Uri baseUri, Uri location)
    {
        // RFC allows relative redirects
        if (location.IsAbsoluteUri) return location;
        return new Uri(baseUri, location);
    }

    private static UrlResolutionError MapHttpException(HttpRequestException ex, UrlResolutionOptions options)
    {
        // TLS failures often surface as AuthenticationException inner
        if (ex.InnerException is AuthenticationException)
        {
            return new UrlResolutionError(UrlErrorCode.TlsFailure,
                "TLS handshake/certificate validation failed for the HTTPS endpoint.");
        }

        if (ex.InnerException is SocketException se)
        {
            // DNS failures commonly appear as HostNotFound / TryAgain
            if (se.SocketErrorCode is SocketError.HostNotFound or SocketError.TryAgain)
            {
                return new UrlResolutionError(UrlErrorCode.DnsFailure,
                    "DNS resolution failed for the provided hostname.");
            }

            // Connection refused
            if (se.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return new UrlResolutionError(UrlErrorCode.ConnectionRefused,
                    "Connection was refused by the remote host.");
            }

            // Timeouts (connect) can show as TimedOut depending on platform
            if (se.SocketErrorCode == SocketError.TimedOut)
            {
                return new UrlResolutionError(UrlErrorCode.ConnectionTimeout,
                    $"Connection timed out after {options.ConnectTimeout.TotalSeconds:0.#}s.");
            }
        }

        // Fallback: user-friendly, generic transport error
        return new UrlResolutionError(UrlErrorCode.ConnectionTimeout,
            "Network connection failed while trying to reach the URL.");
    }

    private static UrlResolutionResult Fail(
        string original,
        UrlResolutionError error,
        IReadOnlyList<RedirectHop> chain,
        Uri? canonicalScanRoot = null)
    {
        return new UrlResolutionResult
        {
            OriginalSubmittedUrl = original,
            CanonicalScanRootUrl = canonicalScanRoot,
            RedirectChain = chain,
            Error = error
        };
    }
}

public static class HttpClientNames
{
    public const string UrlResolution = "UrlResolution";
}

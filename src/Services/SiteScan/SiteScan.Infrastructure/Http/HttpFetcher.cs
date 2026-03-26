using SiteScan.Application.Crawling;

namespace SiteScan.Infrastructure.Http;

public sealed class HttpFetcher : IHttpFetcher
{
    private readonly HttpClient _client;

    public HttpFetcher(HttpClient client)
    {
        _client = client;
    }

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("SiteScanBot/1.0");

        using var res = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var finalUrl = res.RequestMessage?.RequestUri ?? url;

        byte[] body = await res.Content.ReadAsByteArrayAsync(ct);
        var contentType = res.Content.Headers.ContentType?.ToString();

        // Capture all response headers (response-level + content-level).
        // Multi-value headers are joined with ", " per HTTP convention.
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in res.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in res.Content.Headers)
            headers[h.Key] = string.Join(", ", h.Value);

        return new FetchResult(
            RequestedUrl: url,
            FinalUrl: finalUrl,
            StatusCode: (int)res.StatusCode,
            ContentType: contentType,
            Body: body,
            ResponseHeaders: headers
        );
    }
}

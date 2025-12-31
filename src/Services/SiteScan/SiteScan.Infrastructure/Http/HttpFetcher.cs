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

        return new FetchResult(
            RequestedUrl: url,
            FinalUrl: finalUrl,
            StatusCode: (int)res.StatusCode,
            ContentType: contentType,
            Body: body
        );
    }
}

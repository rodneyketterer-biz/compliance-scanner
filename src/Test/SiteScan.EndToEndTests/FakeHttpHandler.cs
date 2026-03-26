using System.Net;
using System.Text;

namespace SiteScan.EndToEndTests;

/// <summary>
/// An in-memory <see cref="HttpMessageHandler"/> that returns pre-configured responses.
/// Unregistered URLs return 404. Shared by both <c>HttpFetcher</c> and <c>RobotsPolicy</c>
/// so the same fake site is visible to every component in the crawl stack.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, FakeResponse> _responses =
        new(StringComparer.Ordinal);

    // ── Registration helpers ────────────────────────────────────────────────

    /// <summary>Register an HTML page response at the given absolute URL.</summary>
    public void AddPage(string url, string htmlBody) =>
        _responses[url] = new FakeResponse(200, "text/html", htmlBody);

    /// <summary>Register a non-HTML resource (CSS, image, etc.) at the given absolute URL.</summary>
    public void AddResource(string url, string contentType, string body = "") =>
        _responses[url] = new FakeResponse(200, contentType, body);

    /// <summary>
    /// Register a robots.txt response for the host derived from <paramref name="baseUrl"/>.
    /// The robots URL is built the same way <c>RobotsPolicy</c> builds it:
    /// <c>{scheme}://{host}[:{port}]/robots.txt</c> (port omitted when it is the default).
    /// </summary>
    public void AddRobots(string baseUrl, string robotsContent)
    {
        var uri = new Uri(baseUrl);
        var portPart = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var robotsUrl = $"{uri.Scheme}://{uri.Host}{portPart}/robots.txt";
        _responses[robotsUrl] = new FakeResponse(200, "text/plain", robotsContent);
    }

    // ── HttpMessageHandler ──────────────────────────────────────────────────

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var key = request.RequestUri!.AbsoluteUri;

        if (_responses.TryGetValue(key, out var cfg))
        {
            var response = new HttpResponseMessage((HttpStatusCode)cfg.StatusCode)
            {
                Content = new StringContent(cfg.Body, Encoding.UTF8, cfg.ContentType)
            };
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // ── Internal record ─────────────────────────────────────────────────────

    private sealed record FakeResponse(int StatusCode, string ContentType, string Body);
}

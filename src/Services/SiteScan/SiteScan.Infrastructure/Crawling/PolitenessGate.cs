using System.Collections.Concurrent;
using SiteScan.Application.Crawling;

namespace SiteScan.Infrastructure.Crawling;

public sealed class PolitenessGate : IPolitenessGate
{
    private readonly CrawlerOptions _options;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostSemaphores = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _hostLastRequestUtc = new();

    public PolitenessGate(CrawlerOptions options) => _options = options;

    public async Task WaitTurnAsync(Uri url, CancellationToken ct)
    {
        var hostKey = HostKey(url);
        var sem = _hostSemaphores.GetOrAdd(hostKey, _ => new SemaphoreSlim(_options.MaxConcurrencyPerHost, _options.MaxConcurrencyPerHost));

        await sem.WaitAsync(ct);

        try
        {
            // Enforce min delay between requests per host
            var now = DateTimeOffset.UtcNow;
            var last = _hostLastRequestUtc.GetOrAdd(hostKey, _ => DateTimeOffset.MinValue);

            var elapsed = now - last;
            var remaining = _options.MinDelayBetweenRequestsPerHost - elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct);

            _hostLastRequestUtc[hostKey] = DateTimeOffset.UtcNow;
        }
        finally
        {
            // Release immediately after scheduling? No—this gate is applied just before fetch.
            // Since fetch happens right after, you can choose to hold until fetch completes.
            // For simplicity, we release after delay and rely on caller to fetch right away.
            sem.Release();
        }
    }

    private static string HostKey(Uri u) => $"{u.Scheme}://{u.Host}:{u.Port}";
}

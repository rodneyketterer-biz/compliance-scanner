using System.Collections.Concurrent;
using SiteScan.Application.Crawling;
using SiteScan.Domain.Scanning;

namespace SiteScan.Infrastructure.Persistence;

public sealed class InMemoryCrawlStore : ICrawlRecordWriter, ICrawlRecordReader
{
    private readonly ConcurrentDictionary<ScanId, ConcurrentBag<CrawlRecord>> _store = new();

    public Task WriteAsync(CrawlRecord record, CancellationToken ct)
    {
        var bag = _store.GetOrAdd(record.ScanId, _ => new ConcurrentBag<CrawlRecord>());
        bag.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrawlRecord>> GetByScanIdAsync(ScanId scanId, CancellationToken ct)
    {
        if (!_store.TryGetValue(scanId, out var bag))
            return Task.FromResult<IReadOnlyList<CrawlRecord>>(Array.Empty<CrawlRecord>());

        // Example ordering: time asc
        var list = bag.OrderBy(r => r.TimestampUtc).ToList();
        return Task.FromResult<IReadOnlyList<CrawlRecord>>(list);
    }
}

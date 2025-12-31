using System.Collections.Concurrent;

namespace SiteScan.Application.Crawling;

internal sealed class TrapDetector
{
    private readonly int _maxUrlLength;
    private readonly int _maxQueryCombosPerPath;

    // path-key -> distinct normalized query signatures
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _queryCombosByPath = new();

    public TrapDetector(int maxUrlLength, int maxQueryCombosPerPath)
    {
        _maxUrlLength = maxUrlLength;
        _maxQueryCombosPerPath = maxQueryCombosPerPath;
    }

    public bool IsUrlTooLong(Uri url) => url.AbsoluteUri.Length > _maxUrlLength;

    public bool ExceedsQueryCombos(Uri url)
    {
        // “Common traps” heuristic: limit distinct query parameter combinations per path.
        // We intentionally use the *exact* query string as provided, but we normalize a signature
        // that treats different ordering as different combos (since we are preserving query exactly).
        // If you want order-insensitive combos, normalize here.
        var pathKey = $"{url.Scheme}://{url.Host}:{url.Port}{url.AbsolutePath}";
        var querySig = url.Query ?? string.Empty;

        var set = _queryCombosByPath.GetOrAdd(pathKey, _ => new ConcurrentDictionary<string, byte>());
        set.TryAdd(querySig, 0);

        return set.Count > _maxQueryCombosPerPath;
    }
}

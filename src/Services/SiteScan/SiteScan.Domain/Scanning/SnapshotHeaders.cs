namespace SiteScan.Domain.Scanning;

/// <summary>
/// Store either full headers or an allowlist. This implementation stores an allowlist + Content-Type/ETag.
/// </summary>
public sealed class SnapshotHeaders
{
    private SnapshotHeaders() { } // EF

    public SnapshotHeaders(IReadOnlyDictionary<string, string> headers)
    {
        // normalize casing for keys; keep last value; store as plain strings.
        Headers = headers
            .GroupBy(kvp => kvp.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> Headers { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}
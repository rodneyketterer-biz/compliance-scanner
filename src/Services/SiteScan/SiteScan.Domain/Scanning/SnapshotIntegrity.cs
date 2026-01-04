namespace SiteScan.Domain.Scanning;

public sealed class SnapshotIntegrity
{
    private SnapshotIntegrity() { } // EF

    public SnapshotIntegrity(string? etag, string? contentHashSha256)
    {
        ETag = etag;
        ContentHashSha256 = contentHashSha256;
    }

    public string? ETag { get; private set; }
    public string? ContentHashSha256 { get; private set; }
}
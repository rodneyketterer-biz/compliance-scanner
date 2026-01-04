namespace SiteScan.Domain.Scanning;

public sealed class SnapshotContent
{
    private SnapshotContent() { } // EF

    public SnapshotContent(
        string storageRef, // points to blob/file/db row
        long originalLengthBytes,
        long storedLengthBytes,
        bool wasTruncated)
    {
        StorageRef = storageRef;
        OriginalLengthBytes = originalLengthBytes;
        StoredLengthBytes = storedLengthBytes;
        WasTruncated = wasTruncated;
    }

    public string StorageRef { get; private set; } = default!;
    public long OriginalLengthBytes { get; private set; }
    public long StoredLengthBytes { get; private set; }
    public bool WasTruncated { get; private set; }
}

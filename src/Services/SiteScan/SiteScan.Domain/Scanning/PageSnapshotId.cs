namespace SiteScan.Domain.Scanning;

public readonly record struct PageSnapshotId(Guid Value)
{
    public static PageSnapshotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

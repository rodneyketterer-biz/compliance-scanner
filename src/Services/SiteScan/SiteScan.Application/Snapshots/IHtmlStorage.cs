namespace SiteScan.Application.Snapshots;

public interface IHtmlSnapshotStorage
{
    /// <summary>Saves HTML bytes and returns a storage reference (opaque string).</summary>
    Task<string> SaveAsync(
        Guid scanId,
        Guid snapshotId,
        ReadOnlyMemory<byte> htmlBytes,
        CancellationToken ct);

    Task<ReadOnlyMemory<byte>> ReadAsync(string storageRef, CancellationToken ct);

    Task DeleteAsync(string storageRef, CancellationToken ct);
}
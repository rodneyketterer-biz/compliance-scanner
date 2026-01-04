using SiteScan.Application.Snapshots;

namespace SiteScan.Infrastructure.Snapshots;

public sealed class FileHtmlSnapshotStorage : IHtmlSnapshotStorage
{
    private readonly string _basePath;

    public FileHtmlSnapshotStorage(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveAsync(Guid scanId, Guid snapshotId, ReadOnlyMemory<byte> htmlBytes, CancellationToken ct)
    {
        // path = {base}/{scanId}/{snapshotId}.html
        var dir = Path.Combine(_basePath, scanId.ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, snapshotId.ToString("N") + ".html");
        await File.WriteAllBytesAsync(path, htmlBytes.ToArray(), ct);

        // storageRef is a relative token
        return $"{scanId:N}/{snapshotId:N}.html";
    }

    public async Task<ReadOnlyMemory<byte>> ReadAsync(string storageRef, CancellationToken ct)
    {
        var path = Path.Combine(_basePath, storageRef.Replace('/', Path.DirectorySeparatorChar));
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return bytes;
    }

    public Task DeleteAsync(string storageRef, CancellationToken ct)
    {
        var path = Path.Combine(_basePath, storageRef.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
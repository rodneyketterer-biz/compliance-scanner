namespace SiteScan.Application.Snapshots;

public sealed class SnapshotOptions
{
    /// <summary>Max HTML bytes stored per page. If exceeded, content is truncated and flagged.</summary>
    public int MaxHtmlBytesPerPage { get; init; } = 512 * 1024; // 512KB default

    /// <summary>Retention for snapshots/failures (used by cleanup job).</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(30);

    /// <summary>If true, store only allowlisted headers; else store full header set.</summary>
    public bool UseHeaderAllowlist { get; init; } = true;

    /// <summary>Where HTML is stored: "db" or "files" (or "blob").</summary>
    public string StorageMode { get; init; } = "files";

    /// <summary>
    /// Root directory for <c>FileHtmlSnapshotStorage</c>.
    /// May be an absolute path or a path relative to the application working directory.
    /// Only used when <see cref="StorageMode"/> is <c>"files"</c>.
    /// </summary>
    public string HtmlStoragePath { get; init; } = "html-snapshots";
}
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SiteScan.Domain.Scanning;
using System.Text.Json;

namespace SiteScan.Infrastructure.Persistence;

public sealed class SiteScanDbContext : DbContext
{
    public SiteScanDbContext(DbContextOptions<SiteScanDbContext> options) : base(options) { }

    public DbSet<PageSnapshot> PageSnapshots => Set<PageSnapshot>();
    public DbSet<FailedFetch> FailedFetches => Set<FailedFetch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PageSnapshot>(b =>
        {
            b.ToTable("page_snapshots");
            b.HasKey(x => x.Id);

            b.Property(x => x.Id)
                .HasConversion(v => v.Value, v => new PageSnapshotId(v));

            b.Property(x => x.ScanId)
                .HasConversion(v => v.Value, v => new ScanId(v))
                .IsRequired();

            b.Property(x => x.FinalUrl).IsRequired().HasMaxLength(2048);
            b.Property(x => x.NormalizedUrlKey).IsRequired().HasMaxLength(2048);
            b.Property(x => x.StatusCode).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(256);
            b.Property(x => x.FetchedAtUtc).IsRequired();
            b.Property(x => x.CrawlDepth).IsRequired();

            // Queryability: scanId + normalizedUrlKey
            b.HasIndex(x => new { x.ScanId, x.NormalizedUrlKey }).IsUnique(false);

            b.OwnsOne(x => x.Content, owned =>
            {
                owned.Property(x => x.StorageRef).HasMaxLength(512);
                owned.Property(x => x.OriginalLengthBytes);
                owned.Property(x => x.StoredLengthBytes);
                owned.Property(x => x.WasTruncated);
            });

            // Headers stored as JSON column
            b.OwnsOne(x => x.Headers, owned =>
            {
                var converter = new ValueConverter<IReadOnlyDictionary<string, string>, string>(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
                         ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                owned.Property(x => x.Headers)
                    .HasConversion(converter)
                    .HasColumnName("headers_json")
                    .HasColumnType("text");
            });

            b.OwnsOne(x => x.Integrity, owned =>
            {
                owned.Property(x => x.ETag).HasMaxLength(256);
                owned.Property(x => x.ContentHashSha256).HasMaxLength(64);
            });
        });

        modelBuilder.Entity<FailedFetch>(b =>
        {
            b.ToTable("failed_fetches");
            b.HasKey(x => x.Id);

            b.Property(x => x.ScanId)
                .HasConversion(v => v.Value, v => new ScanId(v))
                .IsRequired();

            b.Property(x => x.Url).IsRequired().HasMaxLength(2048);
            b.Property(x => x.NormalizedUrlKey).IsRequired().HasMaxLength(2048);
            b.Property(x => x.FailedAtUtc).IsRequired();
            b.Property(x => x.CrawlDepth).IsRequired();
            b.Property(x => x.Reason).IsRequired();
            b.Property(x => x.Message).IsRequired().HasMaxLength(2048);

            b.HasIndex(x => new { x.ScanId, x.NormalizedUrlKey });
        });
    }
}
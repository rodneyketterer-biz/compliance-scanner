using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SiteScan.Application.Abstractions;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.UrlResolution;
using SiteScan.Infrastructure.Http;
using SiteScan.Infrastructure.Persistence;
using SiteScan.Infrastructure.Snapshots;

namespace SiteScan.Infrastructure;

public static class DependencyInjection
{
    // ── URL resolution ──────────────────────────────────────────────────────

    public static IServiceCollection AddUrlResolutionFromOptions(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientNames.UrlResolution)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<UrlResolutionOptions>>().Value;

                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = opts.ConnectTimeout
                };
            });

        services.AddScoped<IUrlResolver, HttpUrlResolver>();
        return services;
    }

    // ── Database ────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="SiteScanDbContext"/> using SQLite.
    /// <para>
    /// Call <c>db.Database.EnsureCreated()</c> during startup (see <c>Program.cs</c>)
    /// until EF Core migrations are introduced (tracked as a known gap).
    /// </para>
    /// </summary>
    public static IServiceCollection AddSiteScanDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<SiteScanDbContext>(opts =>
            opts.UseSqlite(connectionString));
        return services;
    }

    // ── Snapshot storage ────────────────────────────────────────────────────

    /// <summary>
    /// Registers all snapshot-persistence services:
    /// <list type="bullet">
    ///   <item><see cref="IPageSnapshotRepository"/> → <see cref="EfPageSnapshotRepository"/> (scoped)</item>
    ///   <item><see cref="IHtmlSnapshotStorage"/> → <see cref="FileHtmlSnapshotStorage"/> (singleton)</item>
    ///   <item><see cref="ISnapshotPersister"/> → <see cref="SnapshotPersister"/> (scoped)</item>
    /// </list>
    /// Requires <see cref="SiteScanDbContext"/> to be registered first —
    /// call <see cref="AddSiteScanDatabase"/> before this method.
    /// </summary>
    public static IServiceCollection AddSnapshotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind SnapshotOptions from the "Snapshots" configuration section.
        services.Configure<SnapshotOptions>(configuration.GetSection("Snapshots"));

        // Expose the resolved value so SnapshotPersister can accept SnapshotOptions
        // directly (keeping the Application layer free of IOptions<T> dependency).
        services.AddSingleton<SnapshotOptions>(sp =>
            sp.GetRequiredService<IOptions<SnapshotOptions>>().Value);

        // File-based HTML blob storage (singleton — wraps the file system).
        services.AddSingleton<IHtmlSnapshotStorage>(sp =>
        {
            var opts = sp.GetRequiredService<SnapshotOptions>();
            return new FileHtmlSnapshotStorage(opts.HtmlStoragePath);
        });

        services.AddScoped<IPageSnapshotRepository, EfPageSnapshotRepository>();
        services.AddScoped<ISnapshotPersister, SnapshotPersister>();

        return services;
    }
}

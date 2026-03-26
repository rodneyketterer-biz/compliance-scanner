using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SiteScan.Application.Abstractions;
using SiteScan.Application.Crawling;
using SiteScan.Application.Snapshots;
using SiteScan.Domain.Scanning;
using SiteScan.Domain.UrlResolution;
using SiteScan.Infrastructure;
using SiteScan.Infrastructure.Crawling;
using SiteScan.Infrastructure.Html;
using SiteScan.Infrastructure.Http;
using SiteScan.Infrastructure.Persistence;
using SiteScan.Infrastructure.Robots;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI / Swagger ──────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SiteScan Test API",
        Version = "v1",
        Description = "Developer harness for triggering compliance scans and inspecting crawl results."
    }));

// ── URL resolution (validates the scan root URL before crawling begins) ────
builder.Services.Configure<UrlResolutionOptions>(
    builder.Configuration.GetSection("UrlResolution"));
builder.Services.AddUrlResolutionFromOptions();
builder.Services.AddScoped<IUrlResolver, HttpUrlResolver>();

// ── Crawler options (bound from config; all defaults apply if section absent)
builder.Services.Configure<CrawlerOptions>(
    builder.Configuration.GetSection("Crawler"));
// Expose the resolved value directly so it can be injected without IOptions<T>.
builder.Services.AddSingleton<CrawlerOptions>(sp =>
    sp.GetRequiredService<IOptions<CrawlerOptions>>().Value);

// ── Named HttpClient shared by the crawler and robots-policy ──────────────
builder.Services.AddHttpClient("Crawler");

// ── Crawler infrastructure (all registered as singletons) ─────────────────
// SimpleRegistrableDomainResolver: last-two-labels eTLD+1 approximation.
builder.Services.AddSingleton<IRegistrableDomainResolver, SimpleRegistrableDomainResolver>();
builder.Services.AddSingleton<IUrlCanonicalizer, SiteScan.Application.Crawling.UrlCanonicalizer>();
builder.Services.AddSingleton<IScopePolicy>(sp =>
    new ScopePolicy(
        sp.GetRequiredService<CrawlerOptions>(),
        sp.GetRequiredService<IRegistrableDomainResolver>()));
builder.Services.AddSingleton<IRobotsPolicy>(sp =>
    new RobotsPolicy(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Crawler"),
        sp.GetRequiredService<CrawlerOptions>()));
builder.Services.AddSingleton<IPolitenessGate>(sp =>
    new PolitenessGate(sp.GetRequiredService<CrawlerOptions>()));
builder.Services.AddSingleton<IHttpFetcher>(sp =>
    new HttpFetcher(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Crawler")));
builder.Services.AddSingleton<IHtmlLinkExtractor, AngleSharpLinkExtractor>();

// ── In-memory crawl store — shared singleton so all scans accumulate here ─
var crawlStore = new InMemoryCrawlStore();
builder.Services.AddSingleton<ICrawlRecordWriter>(crawlStore);
builder.Services.AddSingleton<ICrawlRecordReader>(crawlStore);

// ── CrawlerFactory — creates a fresh Crawler instance per scan ────────────
builder.Services.AddSingleton<CrawlerFactory>();

// ── ScanRegistry — tracks running / completed scan states ─────────────────
builder.Services.AddSingleton<ScanRegistry>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = string.Empty;
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    });
}

app.UseHttpsRedirection();

// ────────────────────────────────────────────────────────────────────────────
// POST /scans
//
// Accepts a URL, validates and canonicalizes it, registers a new scan, fires
// the crawl in the background, and immediately returns the scan identifier.
//
// Callers can poll GET /scans/{scanId} to check status and then call
// GET /scans/{scanId}/pages to inspect individual crawl records.
// ────────────────────────────────────────────────────────────────────────────
app.MapPost("/scans", async (
    StartScanRequest? request,
    IUrlResolver urlResolver,
    IOptions<UrlResolutionOptions> urlOpts,
    CrawlerFactory crawlerFactory,
    ScanRegistry registry,
    CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Url))
        return Results.BadRequest(new ErrorResponse("MissingUrl", "A URL is required."));

    var resolution = await urlResolver.ValidateNormalizeAndResolveAsync(
        request.Url, urlOpts.Value, ct);

    if (!resolution.Success)
        return Results.BadRequest(new ErrorResponse(
            resolution.Error!.Code.ToString(),
            resolution.Error.Message));

    var scanId       = ScanId.New();
    var canonicalRoot = resolution.CanonicalScanRootUrl!;

    registry.Register(scanId, request.Url, canonicalRoot.ToString());

    // Build a new Crawler bound to this scan and run it on a background thread.
    // CancellationToken.None is intentional: the crawl must outlive the request.
    var crawler = crawlerFactory.Create();
    _ = Task.Run(async () =>
    {
        try
        {
            await crawler.RunAsync(scanId, canonicalRoot, CancellationToken.None);
            registry.MarkCompleted(scanId);
        }
        catch (Exception ex)
        {
            registry.MarkFailed(scanId, $"{ex.GetType().Name}: {ex.Message}");
        }
    });

    return Results.Accepted(
        $"/scans/{scanId.Value:D}",
        new StartScanResponse(scanId.Value, request.Url, canonicalRoot.ToString()));
})
.WithName("StartScan")
.WithSummary("Start a new compliance scan")
.WithDescription("Validates the submitted URL, starts a background crawl, and returns the scan ID.")
.WithOpenApi();

// ────────────────────────────────────────────────────────────────────────────
// GET /scans/{scanId}
//
// Returns the current status (running / completed / failed) for a scan plus a
// high-level summary of how many pages were fetched or skipped.
// ────────────────────────────────────────────────────────────────────────────
app.MapGet("/scans/{scanId}", async (
    string scanId,
    ScanRegistry registry,
    ICrawlRecordReader reader,
    CancellationToken ct) =>
{
    if (!Guid.TryParse(scanId, out var guid))
        return Results.BadRequest(new ErrorResponse("InvalidId", "Scan ID must be a valid GUID."));

    var id    = new ScanId(guid);
    var state = registry.Get(id);
    if (state is null)
        return Results.NotFound(new ErrorResponse("NotFound", $"Scan '{scanId}' not found."));

    var records = await reader.GetByScanIdAsync(id, ct);
    var fetched = records.Count(r => r.Disposition == CrawlDisposition.Fetched);
    var skipped = records.Count(r => r.Disposition == CrawlDisposition.Skipped);

    return Results.Ok(new ScanSummaryResponse(
        ScanId:          guid,
        StartUrl:        state.StartUrl,
        CanonicalRootUrl: state.CanonicalRootUrl,
        Status:          state.Status,
        StartedAt:       state.StartedAt,
        CompletedAt:     state.CompletedAt,
        Error:           state.Error,
        PagesFetched:    fetched,
        PagesSkipped:    skipped,
        TotalRecords:    records.Count));
})
.WithName("GetScan")
.WithSummary("Get scan status and summary")
.WithDescription("Returns current status (running/completed/failed) and aggregate counts for a scan.")
.WithOpenApi();

// ────────────────────────────────────────────────────────────────────────────
// GET /scans/{scanId}/pages
//
// Returns every CrawlRecord written during the scan — both fetched pages and
// skipped URLs — ordered by timestamp ascending.
// ────────────────────────────────────────────────────────────────────────────
app.MapGet("/scans/{scanId}/pages", async (
    string scanId,
    ScanRegistry registry,
    ICrawlRecordReader reader,
    CancellationToken ct) =>
{
    if (!Guid.TryParse(scanId, out var guid))
        return Results.BadRequest(new ErrorResponse("InvalidId", "Scan ID must be a valid GUID."));

    var id = new ScanId(guid);
    if (registry.Get(id) is null)
        return Results.NotFound(new ErrorResponse("NotFound", $"Scan '{scanId}' not found."));

    var records = await reader.GetByScanIdAsync(id, ct);
    var pages = records.Select(r => new PageRecordResponse(
        Url:          r.Url.ToString(),
        FinalUrl:     r.FinalUrl?.ToString(),
        StatusCode:   r.StatusCode,
        ContentType:  r.ContentType,
        Depth:        r.Depth,
        Disposition:  r.Disposition.ToString(),
        SkipReason:   r.SkipReason == SkipReason.None ? null : r.SkipReason.ToString(),
        Notes:        r.Notes,
        TimestampUtc: r.TimestampUtc
    )).ToList();

    return Results.Ok(pages);
})
.WithName("GetScanPages")
.WithSummary("List all crawl records for a scan")
.WithDescription("Returns every fetched and skipped URL recorded during the crawl, ordered by timestamp.")
.WithOpenApi();

app.Run();

// ── Request / Response DTOs ──────────────────────────────────────────────────

public sealed record StartScanRequest(string? Url);

public sealed record StartScanResponse(
    Guid   ScanId,
    string StartUrl,
    string CanonicalRootUrl);

public sealed record ScanSummaryResponse(
    Guid             ScanId,
    string           StartUrl,
    string           CanonicalRootUrl,
    string           Status,
    DateTimeOffset   StartedAt,
    DateTimeOffset?  CompletedAt,
    string?          Error,
    int              PagesFetched,
    int              PagesSkipped,
    int              TotalRecords);

public sealed record PageRecordResponse(
    string          Url,
    string?         FinalUrl,
    int?            StatusCode,
    string?         ContentType,
    int             Depth,
    string          Disposition,
    string?         SkipReason,
    string?         Notes,
    DateTimeOffset  TimestampUtc);

public sealed record ErrorResponse(string Code, string Message);

// ── ScanState / ScanRegistry ─────────────────────────────────────────────────

/// <summary>Mutable state for a single scan tracked by <see cref="ScanRegistry"/>.</summary>
public sealed class ScanState
{
    public required ScanId          ScanId          { get; init; }
    public required string          StartUrl        { get; init; }
    public required string          CanonicalRootUrl { get; init; }
    public required DateTimeOffset  StartedAt       { get; init; }
    public          DateTimeOffset? CompletedAt     { get; set; }
    public          string?         Error           { get; set; }

    /// <summary>One of: <c>running</c>, <c>completed</c>, <c>failed</c>.</summary>
    public string Status => Error is not null
        ? "failed"
        : CompletedAt.HasValue ? "completed" : "running";
}

/// <summary>
/// Thread-safe store for scan lifecycle state (running / completed / failed).
/// Injected as a singleton; the crawler background task updates it when done.
/// </summary>
public sealed class ScanRegistry
{
    private readonly ConcurrentDictionary<ScanId, ScanState> _scans = new();

    public void Register(ScanId id, string startUrl, string canonicalRootUrl)
        => _scans[id] = new ScanState
        {
            ScanId           = id,
            StartUrl         = startUrl,
            CanonicalRootUrl = canonicalRootUrl,
            StartedAt        = DateTimeOffset.UtcNow
        };

    public ScanState? Get(ScanId id)
        => _scans.TryGetValue(id, out var state) ? state : null;

    public void MarkCompleted(ScanId id)
    {
        if (_scans.TryGetValue(id, out var state))
            state.CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(ScanId id, string error)
    {
        if (_scans.TryGetValue(id, out var state))
        {
            state.Error       = error;
            state.CompletedAt = DateTimeOffset.UtcNow;
        }
    }
}

// ── CrawlerFactory ────────────────────────────────────────────────────────────

/// <summary>
/// Creates a fresh <see cref="Crawler"/> instance for each scan run.
/// All crawler infrastructure dependencies are singletons injected once at
/// construction; <see cref="NullSnapshotPersister"/> is used because the test
/// API does not require snapshot persistence.
/// </summary>
public sealed class CrawlerFactory
{
    private readonly CrawlerOptions      _options;
    private readonly IUrlCanonicalizer   _canonicalizer;
    private readonly IScopePolicy        _scope;
    private readonly IRobotsPolicy       _robots;
    private readonly IPolitenessGate     _politeness;
    private readonly IHttpFetcher        _fetcher;
    private readonly IHtmlLinkExtractor  _links;
    private readonly ICrawlRecordWriter  _writer;

    public CrawlerFactory(
        CrawlerOptions      options,
        IUrlCanonicalizer   canonicalizer,
        IScopePolicy        scope,
        IRobotsPolicy       robots,
        IPolitenessGate     politeness,
        IHttpFetcher        fetcher,
        IHtmlLinkExtractor  links,
        ICrawlRecordWriter  writer)
    {
        _options       = options;
        _canonicalizer = canonicalizer;
        _scope         = scope;
        _robots        = robots;
        _politeness    = politeness;
        _fetcher       = fetcher;
        _links         = links;
        _writer        = writer;
    }

    /// <summary>Returns a new <see cref="Crawler"/> wired to all singleton infrastructure.</summary>
    public Crawler Create()
        => new Crawler(
            _options, _canonicalizer, _scope, _robots,
            _politeness, _fetcher, _links, _writer,
            NullSnapshotPersister.Instance);
}

// ── SimpleRegistrableDomainResolver ──────────────────────────────────────────

/// <summary>
/// Minimal eTLD+1 resolver: returns the last two dot-separated labels of the
/// host.  Sufficient for <see cref="ScopeMode.SameHost"/> (which never calls
/// this) and adequate for basic <see cref="ScopeMode.RegistrableDomain"/> use.
/// </summary>
public sealed class SimpleRegistrableDomainResolver : IRegistrableDomainResolver
{
    public string? TryGetRegistrableDomain(string host)
    {
        var parts = host.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[^2..]) : host;
    }
}

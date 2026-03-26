# CLAUDE.md — Compliance Scanner (SiteScan)

This file documents the codebase structure, development workflows, and conventions for AI assistants working in this repository.

---

## Project Overview

**SiteScan** is an ASP.NET Core Web API that performs website compliance scanning. It validates and normalizes URLs, crawls websites with configurable scope and depth limits, stores page snapshots, and enforces robots.txt compliance and trap detection.

- **Language:** C# 13 / .NET 9.0
- **Architecture:** Clean Architecture (Domain → Application → Infrastructure → API)
- **Testing:** NUnit 4.x
- **SDK pin:** `src/global.json` → `9.0.308`

---

## Repository Layout

```
compliance-scanner/
├── CLAUDE.md                    ← this file
├── README.md
├── .gitignore
├── ComplianceScanner.slnx       ← solution file (all projects)
└── src/
    ├── global.json              ← .NET SDK version pin
    ├── Services/SiteScan/
    │   ├── SiteScan.Api/        ← ASP.NET Core Web API (entry point)
    │   ├── SiteScan.Application/← Use cases, abstractions (ports)
    │   ├── SiteScan.Domain/     ← Core business models, no dependencies
    │   ├── SiteScan.Infrastructure/ ← Adapters: HTTP, EF Core, file storage
    │   └── SiteScan.UnitTests/  ← NUnit unit test suite
    └── Test/
        └── SiteScan.EndToEndTests/ ← NUnit E2E tests (real stack, fake HTTP)
```

---

## Architecture

### Layer Dependency Rules

```
Domain ← Application ← Infrastructure
                    ↑
                   Api
```

- **Domain** — pure C# records and value objects; zero external dependencies.
- **Application** — use-case handlers, interfaces (ports), and crawler engine; depends only on Domain.
- **Infrastructure** — implements Application ports using AngleSharp, EF Core, `HttpClient`, file I/O; depends on Application + Domain.
- **Api** — wires up DI, exposes HTTP endpoints; depends on all layers.

### Key Design Patterns

| Pattern | Where |
|---|---|
| Port/Adapter | `Application/Crawling/Ports.cs` defines all crawler abstractions |
| Repository | `IPageSnapshotRepository`, `EfPageSnapshotRepository` |
| Command/Handler | `CreateScanFromUrlCommand` + `CreateScanFromUrlHandler` |
| Options pattern | `CrawlerOptions`, `UrlResolutionOptions`, `SnapshotOptions` |
| Factory method | `ScanId.New()`, `PageSnapshotId.New()` |
| Null Object | `NullSnapshotPersister.Instance` — no-op `ISnapshotPersister` for tests |

---

## Source Files by Layer

### SiteScan.Domain

All types are records or record structs; immutability is enforced throughout.

| File | Purpose |
|---|---|
| `Scanning/ScanId.cs` | GUID wrapper; `ScanId.New()` factory |
| `Scanning/PageSnapshot.cs` | Root aggregate: metadata + nested value objects |
| `Scanning/SnapshotContent.cs` | Storage reference, size, truncation flag |
| `Scanning/SnapshotHeaders.cs` | Dictionary of preserved HTTP headers |
| `Scanning/SnapshotIntegrity.cs` | ETag + SHA-256 |
| `Scanning/CrawlRecord.cs` | Records one crawl event (disposition + reason) |
| `Scanning/CrawlEnums.cs` | `CrawlDisposition` (Fetched/Skipped), `SkipReason` |
| `Scanning/UrlKey.cs` | Normalized URL for dedup lookups |
| `Scanning/HeadersAllowlist.cs` | Allowlisted headers: CSP, HSTS, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, ETag, Content-Type |
| `Scanning/FailedFetch.cs` | Tracks failed fetch attempts with reason and message |
| `UrlResolution/UrlResolutionResult.cs` | Success/failure + redirect chain |
| `UrlResolution/UrlResolutionError.cs` | Error code + message |
| `UrlResolution/UrlErrorCode.cs` | Enum of resolution error types |
| `UrlResolution/UrlResolutionOptions.cs` | Timeout, max-redirects config |
| `UrlResolution/UrlCanonicalizer.cs` | Domain-level URL normalization logic |
| `UrlResolution/RedirectHop.cs` | Single hop in a redirect chain |

### SiteScan.Application

| File | Purpose |
|---|---|
| `Crawling/Crawler.cs` | Main crawl engine (`ICrawler`); calls `ISnapshotPersister` on success and failure |
| `Crawling/CrawlFrontier.cs` | Thread-safe `ConcurrentQueue`-backed URL frontier |
| `Crawling/CrawlerOptions.cs` | Limits: pages (500), depth (4), wall-clock (5 min), concurrency (2/host), delay (250 ms) |
| `Crawling/Ports.cs` | All crawler ports: `ICrawlRecordWriter/Reader`, `IUrlCanonicalizer`, `IScopePolicy`, `IRobotsPolicy`, `IHtmlLinkExtractor`, `IHttpFetcher`, `IPolitenessGate`; `FetchResult` record (includes `ResponseHeaders`) |
| `Crawling/ScopePolicy.cs` | `SameHost` / `RegistrableDomain` scope modes |
| `Crawling/UrlCanonicalizer.cs` | Application-layer URL normalization |
| `Crawling/TrapDetector.cs` | Detects traps: URL too long, too many query combos per path |
| `Scans/CreateScanFromUrlCommand.cs` | Command record |
| `Scans/CreateScanFromUrlHandler.cs` | Validates URL, creates `ScanRecord` |
| `Snapshots/ISnapshotPersister.cs` | `ISnapshotPersister` port + `NullSnapshotPersister` (null object, use in tests) |
| `Snapshots/IPageSnapshotRepository.cs` | Persistence port |
| `Snapshots/IHtmlStorage.cs` | Blob storage port |
| `Snapshots/PersistFetchedPage.cs` | `SnapshotPersister` — implements `ISnapshotPersister`; stores HTML blob + SHA-256 hash for HTML pages, metadata-only for non-HTML, `FailedFetch` for errors |
| `Snapshots/SnapshotOptions.cs` | Snapshot storage config: `MaxHtmlBytesPerPage`, `UseHeaderAllowlist`, `RetentionDays`, `HtmlStoragePath` |
| `UrlResolution/IUrlResolver.cs` | URL resolution port |

### SiteScan.Infrastructure

| File | Purpose |
|---|---|
| `Http/HttpFetcher.cs` | `IHttpFetcher` via `HttpClient`; UA: `SiteScanBot/1.0`; captures all response + content headers into `FetchResult.ResponseHeaders` |
| `Http/HttpUrlResolver.cs` | `IUrlResolver`; validates, normalizes, follows redirects |
| `Http/DependencyInjection.cs` | `AddUrlResolutionFromOptions()` — named client `"UrlResolution"`, no auto-redirect, decompression enabled |
| `Persistence/SiteScanDbContext.cs` | EF Core 8 DbContext; tables: `page_snapshots`, `failed_fetches` |
| `Persistence/EfPageSnapshotRepository.cs` | EF-backed `IPageSnapshotRepository` |
| `Persistence/InMemoryCrawlStore.cs` | In-memory `ICrawlRecordWriter`/`Reader` |
| `Persistence/FileHtmlSnapshotStorage.cs` | File-system `IHtmlStorage` |
| `HtmlParsing/AngleSharpLinkExtractor.cs` | `IHtmlLinkExtractor`; extracts `<a href>`, `<link href>` |
| `Crawling/PolitenessGate.cs` | Per-host rate limiting |
| `Crawling/RobotsPolicy.cs` | robots.txt fetch and compliance |
| `DependencyInjection.cs` | `AddSiteScanDatabase(connectionString)` — registers `SiteScanDbContext` with SQLite; `AddSnapshotServices(IConfiguration)` — registers `SnapshotOptions`, `FileHtmlSnapshotStorage`, `EfPageSnapshotRepository`, `SnapshotPersister` |

### SiteScan.Api

| File | Purpose |
|---|---|
| `Program.cs` | DI composition root, middleware pipeline, endpoint registration; calls `AddSiteScanDatabase` + `AddSnapshotServices`; calls `db.Database.EnsureCreated()` in Development |

**Current API endpoint:**

```
POST /api/scans/validate-url
Body: { "url": "<string>" }
Returns: { success, canonicalUrl, redirectChain, error }
```

Swagger UI is served at the root `/`.

**Launch profiles:**
- `http`  → `http://localhost:5108`
- `https` → `https://localhost:7108`

### SiteScan.EndToEndTests

End-to-end tests that exercise the full crawler stack (all real production implementations) wired to a `FakeHttpHandler` — no real network requests are made.

| File | Purpose |
|---|---|
| `FakeHttpHandler.cs` | `HttpMessageHandler` subclass; maps URLs to pre-configured `FakeResponse` records; returns 404 for unregistered URLs |
| `CrawlHarness.cs` | Assembles the full crawler stack; exposes `Http` (fake handler) and `WithOptions`; returns `IReadOnlyList<CrawlRecord>` from `InMemoryCrawlStore` |
| `CrawlDiscoveryTests.cs` | 8 E2E scenarios covering: single page, link discovery, out-of-scope links, max depth, duplicate URLs, robots.txt disallow, max pages limit, non-HTML resources |

---

## Development Workflows

### Prerequisites

- .NET 9 SDK (version pinned in `src/global.json` → `9.0.308`)
- No Node.js, Docker, or database setup required for unit tests or E2E tests

### Build

```bash
dotnet build ComplianceScanner.slnx
```

### Run Tests

```bash
dotnet test ComplianceScanner.slnx
```

Code coverage is configured via `coverlet.collector`. Unit tests live in `SiteScan.UnitTests`; end-to-end tests live in `SiteScan.EndToEndTests`.

### Run the API (development)

```bash
dotnet run --project src/Services/SiteScan/SiteScan.Api
```

Then open `http://localhost:5108` for Swagger UI.

### Add a New Project to the Solution

```bash
dotnet sln ComplianceScanner.slnx add <path-to-new.csproj>
```

---

## Testing Conventions

- **Framework:** NUnit 4.2.2 with `NUnit.Analyzers` and `NUnit3TestAdapter`
- **Style:** Arrange / Act / Assert; method naming: `MethodName_Condition_ExpectedOutcome`
- **Mocking:** Hand-rolled test doubles defined within each test file (no Moq/NSubstitute)
  - Examples: `RecordingWriter`, `AllowAllRobots`, `NoOpPoliteness`, `FetcherReturning`, `LinkExtractorReturning`, `SpySnapshotRepository`, `SpyHtmlStorage`
- **Coverage tool:** `coverlet.collector` (included automatically during `dotnet test`)
- **No-op persister:** Use `NullSnapshotPersister.Instance` (from `SiteScan.Application`) in any test that constructs a `Crawler` but does not need to assert on snapshot storage

### Unit Test File Mapping (`SiteScan.UnitTests`)

| Test Class | Unit Under Test |
|---|---|
| `CrawlerTests.cs` | `Crawler.cs` |
| `CrawlFrontierTests.cs` | `CrawlFrontier.cs` |
| `TrapDetectorTests.cs` | `TrapDetector.cs` |
| `ScopePolicyTests.cs` | `ScopePolicy.cs` |
| `SnapshotPersisterTests.cs` | `SnapshotPersister` (`PersistFetchedPage.cs`) |
| `UrlCanonicalizerTests.cs` (Application) | Application `UrlCanonicalizer` |
| `UrlCanonicalizerTests.cs` (Domain) | Domain `UrlCanonicalizer` |
| `CollapseDuplicateSlashesTests.cs` | Path normalization helper |
| `CreateScanFromUrlCommandTests.cs` | Command record |
| `CreateScanFromUrlHandlerTests.cs` | `CreateScanFromUrlHandler` |
| `ScanIdTests.cs` | `ScanId` value object |
| `RedirectHopTests.cs` | `RedirectHop` |
| `CrawlEnumsTests.cs` | Crawl enum values |

### End-to-End Test Scenarios (`SiteScan.EndToEndTests`)

| Test | Scenario |
|---|---|
| `SinglePage_WithNoLinks_RecordsSingleFetch` | Crawl a page with no outbound links |
| `RootWithInternalLinks_DiscoversLinkedPages` | Follow links within the same host |
| `OutOfScopeLink_IsSkipped` | Links to external domains are not followed |
| `MaxDepth_ExceededPages_AreSkipped` | Pages beyond `MaxDepth` are skipped |
| `DuplicateUrl_IsFetchedOnlyOnce` | Canonically identical URLs are deduped |
| `RobotsDisallowed_PathIsSkipped` | `robots.txt` Disallow rules are respected |
| `MaxPagesPerScan_StopsCrawlAfterLimit` | `MaxPagesPerScan` cap is enforced |
| `NonHtmlContent_IsFetchedWithoutFollowingEmbeddedUrls` | Non-HTML resources are fetched but not link-extracted |

---

## Code Conventions

### C# Style

- **Nullable reference types** enabled in all projects (`<Nullable>enable</Nullable>`)
- **Implicit usings** enabled — do not add redundant `using System;` etc.
- **Records** for all DTOs, domain events, and value objects
- **Record structs** (`readonly record struct`) for tiny value objects like `ScanId`
- **Primary constructors** are not used; prefer explicit constructors where needed
- Prefer `init` properties on records over mutable setters
- Interfaces named with `I` prefix and placed in `Ports.cs` within each feature folder
- Options classes suffixed with `Options` and configured via `IOptions<T>` / `IConfiguration`

### Project References

Always add project references in `.csproj` files, not assembly references. The dependency direction must respect the layer rules (Domain has no project references).

### Configuration

- Strongly typed options via the Options pattern
- Infrastructure registers its own services via `DependencyInjection.cs` extension methods
- `Program.cs` only calls extension methods — no raw `new` for services
- `SnapshotPersister` takes `SnapshotOptions` directly (not `IOptions<SnapshotOptions>`) to keep the Application layer free of `Microsoft.Extensions.Options` dependency; DI resolves this by registering `SnapshotOptions` as a singleton via `.Value`

### Entity Framework

- EF Core 8 (not EF Core 9 — do not upgrade without verifying compatibility)
- Table names are `snake_case` (set via fluent API, not conventions)
- JSON columns used for `SnapshotHeaders`
- Value converters defined inline in `OnModelCreating`

### Snapshot Persistence

- `ISnapshotPersister` is the port; `SnapshotPersister` is the Infrastructure implementation
- HTML pages: HTML bytes are stored (truncated to `MaxHtmlBytesPerPage` if needed) via `IHtmlSnapshotStorage`, SHA-256 hash computed over **stored** (possibly truncated) bytes
- Non-HTML pages: metadata row only; no blob stored, no hash
- Failed fetches: `FailedFetch` row written; no `PageSnapshot` row, no blob
- Header allowlist (`UseHeaderAllowlist = true` by default): only `HeadersAllowlist.RuleHeaders` headers are persisted; set to `false` to store all response headers

---

## Crawler Defaults (CrawlerOptions)

| Setting | Default |
|---|---|
| `MaxPagesPerScan` | 500 |
| `MaxDepth` | 4 |
| `MaxWallClockTime` | 5 minutes |
| `ScopeMode` | `SameHost` |
| `MaxUrlLength` | 2048 |
| `MaxDistinctQueryCombosPerPath` | 50 |
| `MaxConcurrencyPerHost` | 2 |
| `MinDelayBetweenRequestsPerHost` | 250 ms |

---

## Key External Dependencies

| Package | Version | Purpose |
|---|---|---|
| `AngleSharp` | 1.4.0 | HTML parsing, link extraction |
| `Microsoft.AspNetCore.OpenApi` | 9.0.11 | OpenAPI generation |
| `Swashbuckle.AspNetCore` | 6.6.0 | Swagger UI |
| `Microsoft.EntityFrameworkCore` | 8.0.8 | ORM / persistence |
| `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.8 | SQLite provider for EF Core |
| `Microsoft.Extensions.Http` | 10.0.1 | Named `HttpClient` factory |
| `NUnit` | 4.2.2 | Test framework |
| `coverlet.collector` | 6.0.2 | Code coverage |

---

## Git Workflow

- Feature branches follow the pattern `claude/<description>-<id>`
- `main` is the primary integration branch
- Commit messages should be imperative and descriptive (e.g., `Add robots.txt compliance to crawler`)
- Do not commit directly to `main`; open a pull request

---

## What Does Not Exist Yet (Known Gaps)

- No database migrations (EF Core schema managed with `EnsureCreated()` — tech debt; tracked for future migration adoption)
- No authentication/authorization on API endpoints
- No background job / hosted service for running scans asynchronously
- `README.md` is a placeholder — not yet populated

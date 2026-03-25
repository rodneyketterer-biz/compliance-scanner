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
└── src/
    ├── global.json              ← .NET SDK version pin
    ├── SiteScan.sln             ← solution file (5 projects)
    └── Services/SiteScan/
        ├── SiteScan.Api/        ← ASP.NET Core Web API (entry point)
        ├── SiteScan.Application/← Use cases, abstractions (ports)
        ├── SiteScan.Domain/     ← Core business models, no dependencies
        ├── SiteScan.Infrastructure/ ← Adapters: HTTP, EF Core, file storage
        └── SiteScan.UnitTests/  ← NUnit test suite
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
| `Scanning/HeadersAllowlist.cs` | Allowlisted headers to persist |
| `Scanning/FailedFetch.cs` | Tracks failed fetch attempts |
| `UrlResolution/UrlResolutionResult.cs` | Success/failure + redirect chain |
| `UrlResolution/UrlResolutionError.cs` | Error code + message |
| `UrlResolution/UrlErrorCode.cs` | Enum of resolution error types |
| `UrlResolution/UrlResolutionOptions.cs` | Timeout, max-redirects config |
| `UrlResolution/UrlCanonicalizer.cs` | Domain-level URL normalization logic |
| `UrlResolution/RedirectHop.cs` | Single hop in a redirect chain |

### SiteScan.Application

| File | Purpose |
|---|---|
| `Crawling/Crawler.cs` | Main crawl engine (`ICrawler`) |
| `Crawling/CrawlFrontier.cs` | Thread-safe `ConcurrentQueue`-backed URL frontier |
| `Crawling/CrawlerOptions.cs` | Limits: pages (500), depth (4), wall-clock (5 min), concurrency (2/host), delay (250 ms) |
| `Crawling/Ports.cs` | All crawler ports: `ICrawlRecordWriter/Reader`, `IUrlCanonicalizer`, `IScopePolicy`, `IRobotsPolicy`, `IHtmlLinkExtractor`, `IHttpFetcher`, `IPolitenessGate` |
| `Crawling/ScopePolicy.cs` | `SameHost` / `RegistrableDomain` scope modes |
| `Crawling/UrlCanonicalizer.cs` | Application-layer URL normalization |
| `Crawling/TrapDetector.cs` | Detects traps: URL too long, too many query combos per path |
| `Scans/CreateScanFromUrlCommand.cs` | Command record |
| `Scans/CreateScanFromUrlHandler.cs` | Validates URL, creates `ScanRecord` |
| `Snapshots/IPageSnapshotRepository.cs` | Persistence port |
| `Snapshots/IHtmlStorage.cs` | Blob storage port |
| `Snapshots/PersistFetchedPage.cs` | Orchestrates snapshot persistence |
| `Snapshots/SnapshotOptions.cs` | Snapshot storage configuration |
| `UrlResolution/IUrlResolver.cs` | URL resolution port |

### SiteScan.Infrastructure

| File | Purpose |
|---|---|
| `Http/HttpFetcher.cs` | `IHttpFetcher` via `HttpClient`; UA: `SiteScanBot/1.0` |
| `Http/HttpUrlResolver.cs` | `IUrlResolver`; validates, normalizes, follows redirects |
| `Http/DependencyInjection.cs` | `AddUrlResolutionFromOptions()` — named client `"UrlResolution"`, no auto-redirect, decompression enabled |
| `Persistence/SiteScanDbContext.cs` | EF Core 8 DbContext; tables: `page_snapshots`, `failed_fetches` |
| `Persistence/EfPageSnapshotRepository.cs` | EF-backed `IPageSnapshotRepository` |
| `Persistence/InMemoryCrawlStore.cs` | In-memory `ICrawlRecordWriter`/`Reader` |
| `Persistence/FileHtmlSnapshotStorage.cs` | File-system `IHtmlStorage` |
| `HtmlParsing/AngleSharpLinkExtractor.cs` | `IHtmlLinkExtractor`; extracts `<a href>`, `<link href>` |
| `Crawling/PolitenessGate.cs` | Per-host rate limiting |
| `Crawling/RobotsPolicy.cs` | robots.txt fetch and compliance |

### SiteScan.Api

| File | Purpose |
|---|---|
| `Program.cs` | DI composition root, middleware pipeline, endpoint registration |

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

---

## Development Workflows

### Prerequisites

- .NET 9 SDK (version pinned in `src/global.json` → `9.0.308`)
- No Node.js, Docker, or database setup required for unit tests

### Build

```bash
dotnet build src/SiteScan.sln
```

### Run Tests

```bash
dotnet test src/SiteScan.sln
```

Code coverage is configured via `coverlet.collector`. All unit tests live in `SiteScan.UnitTests`.

### Run the API (development)

```bash
dotnet run --project src/Services/SiteScan/SiteScan.Api
```

Then open `http://localhost:5108` for Swagger UI.

### Add a New Project to the Solution

```bash
dotnet sln src/SiteScan.sln add <path-to-new.csproj>
```

---

## Testing Conventions

- **Framework:** NUnit 4.2.2 with `NUnit.Analyzers` and `NUnit3TestAdapter`
- **Style:** Arrange / Act / Assert; method naming: `MethodName_Condition_ExpectedOutcome`
- **Mocking:** Hand-rolled test doubles defined within each test file (no Moq/NSubstitute)
  - Examples: `RecordingWriter`, `AllowAllRobots`, `NoOpPoliteness`, `FetcherReturning`, `LinkExtractorReturning`
- **Coverage tool:** `coverlet.collector` (included automatically during `dotnet test`)

### Test File Mapping

| Test Class | Unit Under Test |
|---|---|
| `CrawlerTests.cs` | `Crawler.cs` |
| `CrawlFrontierTests.cs` | `CrawlFrontier.cs` |
| `TrapDetectorTests.cs` | `TrapDetector.cs` |
| `ScopePolicyTests.cs` | `ScopePolicy.cs` |
| `UrlCanonicalizerTests.cs` (Application) | Application `UrlCanonicalizer` |
| `UrlCanonicalizerTests.cs` (Domain) | Domain `UrlCanonicalizer` |
| `CollapseDuplicateSlashesTests.cs` | Path normalization helper |
| `CreateScanFromUrlCommandTests.cs` | Command record |
| `CreateScanFromUrlHandlerTests.cs` | `CreateScanFromUrlHandler` |
| `ScanIdTests.cs` | `ScanId` value object |
| `RedirectHopTests.cs` | `RedirectHop` |
| `CrawlEnumsTests.cs` | Crawl enum values |

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

### Entity Framework

- EF Core 8 (not EF Core 9 — do not upgrade without verifying compatibility)
- Table names are `snake_case` (set via fluent API, not conventions)
- JSON columns used for `SnapshotHeaders`
- Value converters defined inline in `OnModelCreating`

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

- No integration tests or end-to-end tests
- No database migrations (EF Core schema not yet managed with migrations)
- No authentication/authorization on API endpoints
- No background job / hosted service for running scans asynchronously
- `README.md` is a placeholder — not yet populated

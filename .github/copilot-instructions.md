# Copilot / AI Agent Instructions for ComplianceScanner

This repository implements a small site-scanning service (SiteScan) split into clear layers: `Api`, `Application`, `Domain`, `Infrastructure`, and tests. These notes focus on immediately actionable patterns and where to make safe changes.

- **Big picture**: The `Services/SiteScan` project is the product. The Api exposes minimal HTTP endpoints (Minimal APIs in `Program.cs`) that call into `Application` command handlers which use `Domain` types and `Infrastructure` implementations for runtime behavior.

- **Where to change what**:
  - Business rules / orchestration: `Services/SiteScan/SiteScan.Application` (e.g. `CreateScanFromUrlCommand.cs`).
  - Domain models + immutable records: `Services/SiteScan/SiteScan.Domain` (e.g. `UrlResolutionResult.cs`, `UrlResolutionOptions.cs`).
  - Implementation/side-effects: `Services/SiteScan/SiteScan.Infrastructure` (e.g. `Http/HttpUrlResolver.cs`, `DependencyInjection.cs`).
  - HTTP surface: `Services/SiteScan/SiteScan.Api/Program.cs` (Minimal API handlers and Swagger setup).

- **Key integration points / conventions**:
  - DI registration helper: `DependencyInjection.AddUrlResolutionFromOptions()` registers a named `HttpClient` (`HttpClientNames.UrlResolution`) and `IUrlResolver`.
  - The Api loads config section `UrlResolution` (`Program.cs`) and calls `AddUrlResolutionFromOptions()` — prefer using this extension instead of duplicating HttpClient setup.
  - `IUrlResolver` is the primary abstraction for URL validation/resolution. Implementations should return `UrlResolutionResult` and follow the error codes in `UrlResolutionResult` / `UrlResolutionError`.
  - Records are used widely for DTOs and domain values — follow the same immutable, init-only style.

- **Network behavior details agents must respect**:
  - `HttpUrlResolver` disables automatic redirect handling and enforces connect/overall timeouts via `UrlResolutionOptions`. Redirects are handled manually and mapped to specific `UrlErrorCode` values. Do not re-enable `AllowAutoRedirect` unless intentionally changing redirect semantics.
  - Named client constant: `HttpClientNames.UrlResolution` (see `HttpUrlResolver.cs`). Use it when creating or configuring clients.

- **Testing patterns**:
  - Unit tests use small fakes for `IUrlResolver` (see `SiteScan.UnitTests`), not full network calls. When adding tests, prefer injecting a fake resolver or using `HttpMessageHandler` test stubs for `IHttpClientFactory` rather than live network integration.

- **Build / run / test commands** (from repository root):
  - Build solution: `cd src` then `dotnet build SiteScan.sln`
  - Run API locally: `dotnet run --project Services/SiteScan/SiteScan.Api/SiteScan.Api.csproj` (Swagger UI served at root in Development)
  - Run unit tests: `dotnet test src/Services/SiteScan/SiteScan.UnitTests/SiteScan.UnitTests.csproj` or `dotnet test src/SiteScan.sln`

- **Files you will frequently consult**:
  - `Services/SiteScan/SiteScan.Api/Program.cs` — minimal API endpoints and config binding
  - `Services/SiteScan/SiteScan.Infrastructure/DependencyInjection.cs` — DI helpers and named client configuration
  - `Services/SiteScan/SiteScan.Infrastructure/Http/HttpUrlResolver.cs` — network resolution logic and error mapping
  - `Services/SiteScan/SiteScan.Domain/UrlResolution/*` — options, result and error types
  - `Services/SiteScan/SiteScan.Application/Scans/CreateScanFromUrlCommand.cs` — example of command/handler pattern

- **Agent behavior rules (project-specific)**:
  - Preserve DI extension usage — register services via helpers in `DependencyInjection` where possible.
  - When adding HTTP tests, avoid real network calls. Use fakes or configure a custom `HttpMessageHandler`.
  - Keep configuration in `appsettings.json` / `appsettings.Development.json` and bind to `UrlResolutionOptions` in `Program.cs`.
  - Maintain small, focused public API surface in `SiteScan.Api` (it's intentionally minimal — add endpoints only when needed).

If any area is unclear or you want more examples (e.g., an example unit test using a fake `IUrlResolver`), tell me which part and I will add a short snippet. 

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;
using SiteScan.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Configure OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SiteScan.Api",
        Version = "v1"
    });
});

builder.Services.Configure<UrlResolutionOptions>(builder.Configuration.GetSection("UrlResolution"));
builder.Services.AddUrlResolutionFromOptions();
builder.Services.AddScoped<IUrlResolver, SiteScan.Infrastructure.Http.HttpUrlResolver>();

// Snapshot storage: SQLite database + file-based HTML blobs.
// EnsureCreated is called below (in lieu of migrations; tracked as a known gap).
builder.Services.AddSiteScanDatabase(
    builder.Configuration.GetConnectionString("SiteScan") ?? "Data Source=sitescan.db");
builder.Services.AddSnapshotServices(builder.Configuration);

var app = builder.Build();

// Apply DB schema (no migrations yet).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SiteScan.Infrastructure.Persistence.SiteScanDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = string.Empty; // serve Swagger UI at application root
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    });
}

app.UseHttpsRedirection();

app.MapPost("/api/scans/validate-url",
    async Task<Results<Ok<ValidateUrlResponse>, BadRequest<ValidateUrlResponse>>> (
        ValidateUrlRequest request,
        IUrlResolver resolver,
        IOptions<UrlResolutionOptions> options,
        CancellationToken ct) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            return TypedResults.BadRequest(new ValidateUrlResponse
            {
                Success = false,
                OriginalSubmittedUrl = request?.Url ?? string.Empty,
                CanonicalScanRootUrl = null,
                RedirectChain = new(),
                Error = new ErrorDto("MissingUrl", "A URL is required.")
            });
        }

        var result = await resolver.ValidateNormalizeAndResolveAsync(request.Url, options.Value, ct);

        var response = new ValidateUrlResponse
        {
            Success = result.Success,
            OriginalSubmittedUrl = result.OriginalSubmittedUrl,
            CanonicalScanRootUrl = result.CanonicalScanRootUrl?.ToString(),
            RedirectChain = result.RedirectChain.Select(h => new RedirectHopDto(
                h.Url.ToString(),
                h.StatusCode,
                h.LocationHeader
            )).ToList(),
            Error = result.Success ? null : new ErrorDto(result.Error!.Code.ToString(), result.Error!.Message)
        };

        return result.Success
            ? TypedResults.Ok(response)
            : TypedResults.BadRequest(response);
    })
    .WithName("ValidateUrl")
    .WithOpenApi();

app.Run();

public sealed record ValidateUrlRequest(string Url);

public sealed record ValidateUrlResponse
{
    public required bool Success { get; init; }
    public required string OriginalSubmittedUrl { get; init; }
    public required string? CanonicalScanRootUrl { get; init; }
    public required List<RedirectHopDto> RedirectChain { get; init; }
    public required ErrorDto? Error { get; init; }
}

public sealed record RedirectHopDto(string Url, int StatusCode, string? Location);
public sealed record ErrorDto(string Code, string Message);

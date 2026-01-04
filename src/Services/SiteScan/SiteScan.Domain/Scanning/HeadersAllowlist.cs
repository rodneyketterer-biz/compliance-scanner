namespace SiteScan.Domain.Scanning;

public static class HeadersAllowlist
{
    // Minimum required by AC; you can expand over time.
    public static readonly string[] RuleHeaders =
    [
        "Content-Security-Policy",
        "Strict-Transport-Security",
        "X-Frame-Options",
        "X-Content-Type-Options",
        "Referrer-Policy",
        "ETag",
        "Content-Type"
    ];
}
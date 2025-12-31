namespace SiteScan.Application.Crawling;

public interface IRegistrableDomainResolver
{
    // Returns eTLD+1 for a host (e.g., "sub.example.co.uk" -> "example.co.uk")
    string? TryGetRegistrableDomain(string host);
}

public sealed class ScopePolicy : IScopePolicy
{
    private readonly CrawlerOptions _options;
    private readonly IRegistrableDomainResolver _registrable;

    public ScopePolicy(CrawlerOptions options, IRegistrableDomainResolver registrable)
    {
        _options = options;
        _registrable = registrable;
    }

    public bool IsInScope(Uri candidateCanonical, Uri scanRootCanonical, out string? notes)
    {
        notes = null;

        if (_options.ScopeMode == ScopeMode.SameHost)
        {
            var ok = string.Equals(candidateCanonical.Host, scanRootCanonical.Host, StringComparison.OrdinalIgnoreCase)
                     && candidateCanonical.Port == scanRootCanonical.Port
                     && string.Equals(candidateCanonical.Scheme, scanRootCanonical.Scheme, StringComparison.OrdinalIgnoreCase);

            if (!ok) notes = "ScopeMode=SameHost; candidate host/port/scheme does not match scan root.";
            return ok;
        }

        // Registrable domain mode
        var c = _registrable.TryGetRegistrableDomain(candidateCanonical.Host);
        var r = _registrable.TryGetRegistrableDomain(scanRootCanonical.Host);

        if (c is null || r is null)
        {
            notes = "Registrable domain could not be determined; treating as out-of-scope.";
            return false;
        }

        var ok2 = string.Equals(c, r, StringComparison.OrdinalIgnoreCase);
        if (!ok2) notes = "ScopeMode=RegistrableDomain; candidate registrable domain does not match scan root.";
        return ok2;
    }
}

using System.Collections.Concurrent;
using SiteScan.Application.Crawling;

namespace SiteScan.Infrastructure.Robots;

public sealed class RobotsPolicy : IRobotsPolicy
{
    private readonly HttpClient _client;
    private readonly CrawlerOptions _options;

    private readonly ConcurrentDictionary<string, RobotsRules> _cache = new();

    public RobotsPolicy(HttpClient client, CrawlerOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<RobotsDecision> CanFetchAsync(Uri url, CancellationToken ct)
    {
        var hostKey = $"{url.Scheme}://{url.Host}:{url.Port}";

        var rules = _cache.TryGetValue(hostKey, out var cached)
            ? cached
            : await FetchAndParseAsync(url, ct);

        var allowed = rules.IsAllowed(_options.UserAgent, url.AbsolutePath);
        var notes = allowed ? null : "Disallowed by robots.txt";
        return new RobotsDecision(allowed, notes, rules.Sitemaps);
    }

    private async Task<RobotsRules> FetchAndParseAsync(Uri url, CancellationToken ct)
    {
        var robotsUrl = new Uri($"{url.Scheme}://{url.Host}{(url.IsDefaultPort ? "" : ":" + url.Port)}/robots.txt");
        RobotsRules rules;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            req.Headers.UserAgent.ParseAdd(_options.UserAgent);

            using var res = await _client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                rules = RobotsRules.AllowAll();
            }
            else
            {
                var text = await res.Content.ReadAsStringAsync(ct);
                rules = RobotsRules.Parse(text, robotsUrl);
            }
        }
        catch
        {
            rules = RobotsRules.AllowAll(); // fail-open is typical; you may choose fail-closed
        }

        var hostKey = $"{url.Scheme}://{url.Host}:{url.Port}";
        _cache[hostKey] = rules;
        return rules;
    }

    private sealed class RobotsRules
    {
        private readonly Dictionary<string, List<string>> _disallowByAgent;
        public IReadOnlyList<Uri> Sitemaps { get; }

        private RobotsRules(Dictionary<string, List<string>> disallowByAgent, List<Uri> sitemaps)
        {
            _disallowByAgent = disallowByAgent;
            Sitemaps = sitemaps;
        }

        public static RobotsRules AllowAll() => new(new(StringComparer.OrdinalIgnoreCase), new List<Uri>());

        public static RobotsRules Parse(string content, Uri robotsUri)
        {
            var disallow = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var sitemaps = new List<Uri>();

            string? currentAgent = null;

            using var sr = new StringReader(content);
            while (sr.ReadLine() is { } line)
            {
                var trimmed = line.Split('#')[0].Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var parts = trimmed.Split(':', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
                {
                    currentAgent = value;
                    if (!disallow.ContainsKey(currentAgent))
                        disallow[currentAgent] = new List<string>();
                }
                else if (key.Equals("Disallow", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentAgent is null) continue;
                    // empty Disallow means allow all
                    if (!string.IsNullOrEmpty(value))
                        disallow[currentAgent].Add(value);
                }
                else if (key.Equals("Sitemap", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(value, UriKind.Absolute, out var sm))
                        sitemaps.Add(sm);
                    else if (Uri.TryCreate(robotsUri, value, out var smRel))
                        sitemaps.Add(smRel);
                }
            }

            return new RobotsRules(disallow, sitemaps);
        }

        public bool IsAllowed(string userAgent, string path)
        {
            // Prefer exact UA rules, otherwise fallback to '*'
            if (TryMatch(userAgent, path, out var allowed)) return allowed;
            if (TryMatch("*", path, out allowed)) return allowed;

            return true;
        }

        private bool TryMatch(string agent, string path, out bool allowed)
        {
            allowed = true;
            if (!_disallowByAgent.TryGetValue(agent, out var rules) || rules.Count == 0)
                return false;

            foreach (var prefix in rules)
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    allowed = false;
                    return true;
                }
            }

            allowed = true;
            return true;
        }
    }
}

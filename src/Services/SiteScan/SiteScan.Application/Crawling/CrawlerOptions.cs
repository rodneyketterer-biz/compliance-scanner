namespace SiteScan.Application.Crawling;

public sealed class CrawlerOptions
{
    public int MaxPagesPerScan { get; init; } = 500;
    public int MaxDepth { get; init; } = 4;
    public TimeSpan MaxWallClockTime { get; init; } = TimeSpan.FromMinutes(5);

    public ScopeMode ScopeMode { get; init; } = ScopeMode.SameHost; // default acceptance criteria
    public int MaxUrlLength { get; init; } = 2048;

    // Trap control: max distinct query combinations per path
    public int MaxDistinctQueryCombosPerPath { get; init; } = 50;

    // Politeness
    public int MaxConcurrencyPerHost { get; init; } = 2;
    public TimeSpan MinDelayBetweenRequestsPerHost { get; init; } = TimeSpan.FromMilliseconds(250);

    // Robots
    public string UserAgent { get; init; } = "SiteScanBot/1.0";
    public bool RecordSitemapsFromRobots { get; init; } = true;
}

public enum ScopeMode
{
    SameHost = 0,          // default
    RegistrableDomain = 1  // eTLD+1 matching
}

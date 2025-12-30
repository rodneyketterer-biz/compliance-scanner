namespace SiteScan.Domain.Scanning;

public enum CrawlDisposition
{
    Fetched = 0,
    Skipped = 1
}

public enum SkipReason
{
    None = 0,
    OutOfScope = 1,
    RobotsDisallowed = 2,
    Duplicate = 3,
    LimitReached_MaxPages = 4,
    LimitReached_MaxDepth = 5,
    LimitReached_MaxTime = 6,
    TrapDetected_UrlTooLong = 7,
    TrapDetected_TooManyQueryCombos = 8,
    InvalidUrl = 9,
    NonHtml_NoLinkExtraction = 10
}

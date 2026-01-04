namespace SiteScan.Domain.Scanning;

public enum FetchFailureReason
{
    None = 0,
    DnsFailure,
    Timeout,
    TlsHandshake,
    HttpProtocol,
    BlockedByRobots,
    MaxSizeExceeded, // if you decide to abort rather than truncate
    Unknown
}

namespace SiteScan.Domain.UrlResolution;

public enum UrlErrorCode
{
    InvalidAbsoluteUri,
    UnsupportedScheme,
    MissingScheme,
    ContainsCredentials,

    RedirectLimitExceeded,
    RedirectMissingLocation,
    RedirectUnsupportedScheme,

    DnsFailure,
    ConnectionTimeout,
    ConnectionRefused,
    TlsFailure,
    RequestTimeout,

    FinalStatusNotAllowed
}

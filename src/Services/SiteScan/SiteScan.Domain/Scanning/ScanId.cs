namespace SiteScan.Domain.Scanning
{
    public readonly record struct ScanId(Guid Value)
    {
        public static ScanId New() => new(Guid.NewGuid());
        public override string ToString() => Value.ToString("N");
    }
}

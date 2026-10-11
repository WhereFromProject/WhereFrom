namespace WhereFrom.Core;

public sealed record ProvenanceReport(
    ProvenanceResult Windows,
    IReadOnlyList<StoredDownload> BrowserDownloads,
    int HistoricalBrowserRecords,
    string? BrowserError)
{
    public IReadOnlyList<StoredDownload> PossibleBrowserDownloads { get; init; } = [];
    public StoredDownload? LatestBrowserDownload => BrowserDownloads.FirstOrDefault();
    public string? BrowserSourceUrl => LatestBrowserDownload?.FinalUrl ?? LatestBrowserDownload?.InitialUrl;
    public string? SourceUrl => Windows.SourceUrl ?? BrowserSourceUrl;
    public string? ReferrerUrl => Windows.ReferrerUrl ?? LatestBrowserDownload?.ReferrerUrl;
    public bool HasProvenance => Windows.HasProvenance || BrowserDownloads.Count > 0 || PossibleBrowserDownloads.Count > 0;
    public bool HasConflict => Windows.SourceUrl is not null && BrowserDownloads.Any(download =>
        (download.FinalUrl ?? download.InitialUrl) is { } url &&
        !string.Equals(ProvenanceUrlSanitizer.Sanitize(Windows.SourceUrl) ?? Windows.SourceUrl,
            url, StringComparison.OrdinalIgnoreCase));
    public string Completeness => SourceUrl is not null ? "sourceUrl"
        : Windows.Zone is not null ? "zoneOnly"
        : PossibleBrowserDownloads.Count > 0 ? "possible"
        : Windows.Status is ProvenanceReadStatus.ReadFailed or ProvenanceReadStatus.AccessDenied
          || BrowserError is not null ? "readFailed" : "unknown";
}

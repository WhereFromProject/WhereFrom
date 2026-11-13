using WhereFrom.Core;

namespace WhereFrom.Storage;

public sealed record DownloadHistoryEntry(long Id, StoredDownload Download)
{
    public string FileName => Path.GetFileName(Download.Path);
    public string? SourceDomain =>
        ProvenanceUrlSanitizer.GetHttpDomain(Download.FinalUrl ?? Download.InitialUrl);
}

public sealed record DownloadHistoryCursor(string CompletedUtc, long Id);

public sealed record DownloadHistoryFilter(string Search = "", string? Browser = null, DateTimeOffset? Since = null);

public sealed record DownloadHistoryPage(
    IReadOnlyList<DownloadHistoryEntry> Entries,
    DownloadHistoryCursor? Next);

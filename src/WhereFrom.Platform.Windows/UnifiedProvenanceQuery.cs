using Microsoft.Data.Sqlite;
using WhereFrom.Core;
using WhereFrom.Storage;

namespace WhereFrom.Platform.Windows;

public enum OriginalDownloadPathStatus
{
    Unavailable,
    DifferentFile,
    DirectMatch,
    Possible,
    ReadFailed
}

public sealed class UnifiedProvenanceQuery(IProvenanceProvider windows, DownloadStore downloads)
{
    public OriginalDownloadPathStatus CheckOriginalDownloadPath(StoredDownload download)
    {
        try
        {
            if ((File.GetAttributes(download.Path) & FileAttributes.Directory) != 0)
                return OriginalDownloadPathStatus.Unavailable;

            var file = new FileInfo(download.Path);
            if (!MatchesMetadata(download, file))
                return OriginalDownloadPathStatus.DifferentFile;
            if (download.FileSha256 is null)
                return OriginalDownloadPathStatus.Possible;

            var sha256 = DownloadStore.TryComputeSha256(download.Path);
            file.Refresh();
            if (!file.Exists)
                return OriginalDownloadPathStatus.Unavailable;
            if (!MatchesMetadata(download, file))
                return OriginalDownloadPathStatus.DifferentFile;
            if (sha256 is null)
                return OriginalDownloadPathStatus.ReadFailed;
            return download.FileSha256 == sha256
                ? OriginalDownloadPathStatus.DirectMatch : OriginalDownloadPathStatus.DifferentFile;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return OriginalDownloadPathStatus.Unavailable;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return OriginalDownloadPathStatus.ReadFailed;
        }
    }

    public ProvenanceReport Inspect(string path) => InspectBrowser(path, windows.Inspect(path));

    public (ProvenanceReport Report, string? RawMetadata) InspectWithWindowsMetadata(string path)
    {
        string? rawMetadata = null;
        var result = windows is WindowsZoneProvider provider
            ? provider.Inspect(path, out rawMetadata)
            : windows.Inspect(path);
        return (InspectBrowser(path, result), rawMetadata);
    }

    private ProvenanceReport InspectBrowser(string path, ProvenanceResult result)
    {
        if (result.Status is ProvenanceReadStatus.FileNotFound or ProvenanceReadStatus.InvalidPath
            or ProvenanceReadStatus.NotAFile or ProvenanceReadStatus.AccessDenied)
            return new(result, [], 0, null);

        try
        {
            var file = new FileInfo(path);
            var records = downloads.FindByPath(path);
            file.Refresh();
            var size = file.Length;
            var lastWrite = file.LastWriteTimeUtc;
            var shouldHash = records.Any(record => record.FileSha256 is not null)
                || downloads.HasHashedRecord(size);
            var sha256 = shouldHash ? DownloadStore.TryComputeSha256(path) : null;
            file.Refresh();
            if (file.Length != size || file.LastWriteTimeUtc != lastWrite)
                sha256 = null;
            var sameMetadata = records.Where(record => MatchesMetadata(record, file)).ToArray();
            var matches = sameMetadata.Where(record =>
                    record.FileSha256 is not null && record.FileSha256 == sha256)
                .Select(record => record with { MatchBasis = "sha256" })
                .ToArray();
            var legacyPossible = sameMetadata.Where(record => record.FileSha256 is null)
                .Select(record => record with { MatchBasis = "metadata" });
            var contentPossible = sha256 is null
                ? []
                : downloads.FindByContent(path, file.Length, sha256)
                    .Select(record => record with { MatchBasis = "sha256" }).ToArray();
            return new ProvenanceReport(result, matches,
                records.Count - matches.Length - legacyPossible.Count(), null)
            {
                PossibleBrowserDownloads = legacyPossible.Concat(contentPossible).ToArray()
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or SqliteException or InvalidDataException or FormatException)
        {
            return new(result, [], 0, "storage_unavailable");
        }
    }

    private static bool MatchesMetadata(StoredDownload download, FileInfo file) =>
        download.FileSize == file.Length && download.FileLastWriteUtc.UtcDateTime == file.LastWriteTimeUtc;
}

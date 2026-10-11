using Microsoft.Data.Sqlite;
using WhereFrom.Core;
using WhereFrom.Storage;

namespace WhereFrom.Platform.Windows;

public sealed class UnifiedProvenanceQuery(IProvenanceProvider windows, DownloadStore downloads)
{
    public ProvenanceReport Inspect(string path)
    {
        var result = windows.Inspect(path);
        if (result.Status is ProvenanceReadStatus.FileNotFound or ProvenanceReadStatus.InvalidPath
            or ProvenanceReadStatus.NotAFile or ProvenanceReadStatus.AccessDenied)
            return new(result, [], 0, null);

        try
        {
            var file = new FileInfo(path);
            var records = downloads.FindByPath(path);
            file.Refresh();
            var shouldHash = records.Any(record => record.FileSha256 is not null)
                || downloads.HasHashedRecord(file.Length);
            var sha256 = shouldHash ? DownloadStore.TryComputeSha256(path) : null;
            file.Refresh();
            var sameMetadata = records.Where(record =>
                    record.FileSize == file.Length
                    && record.FileLastWriteUtc.UtcDateTime == file.LastWriteTimeUtc)
                .ToArray();
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
            or ArgumentException or SqliteException or InvalidDataException)
        {
            return new(result, [], 0, "storage_unavailable");
        }
    }
}

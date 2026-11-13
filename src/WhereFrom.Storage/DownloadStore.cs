using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using WhereFrom.Core;

namespace WhereFrom.Storage;

public sealed class DownloadStore(string databasePath)
{
    // Hashing is synchronous and bounded. Larger files keep metadata-only records.
    public const long MaxHashBytes = 64L * 1024 * 1024;

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WhereFrom", "wherefrom.db");

    public bool Save(BrowserDownload download)
    {
        if (download.Incognito)
        {
            return false;
        }
        if (download.Browser is not ("chrome" or "edge")
            || string.IsNullOrWhiteSpace(download.DownloadId)
            || download.StartedAt > download.CompletedAt)
        {
            throw new ArgumentException("Invalid browser download event.", nameof(download));
        }

        var initialUrl = ProvenanceUrlSanitizer.Sanitize(download.InitialUrl);
        var finalUrl = ProvenanceUrlSanitizer.Sanitize(download.FinalUrl);
        var referrerUrl = ProvenanceUrlSanitizer.Sanitize(download.ReferrerUrl);
        if (initialUrl is null && finalUrl is null && referrerUrl is null)
        {
            return false;
        }

        var path = Path.GetFullPath(download.Path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Downloaded file not found.", path);
        }
        var originalSize = file.Length;
        var originalLastWrite = file.LastWriteTimeUtc;
        var sha256 = TryComputeSha256(path);

        using var connection = Open(SqliteOpenMode.ReadWriteCreate);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO downloads
              (browser, download_id, path, path_key, started_utc, completed_utc,
               file_size, file_last_write_utc, initial_url, final_url, referrer_url, file_sha256)
            VALUES
              ($browser, $download_id, $path, $path_key, $started, $completed,
               $size, $last_write, $initial, $final, $referrer, $sha256)
            ON CONFLICT(browser, download_id, started_utc, path_key) DO NOTHING;
            """;
        file.Refresh();
        if (!file.Exists)
        {
            throw new FileNotFoundException("Downloaded file not found.", path);
        }
        if (file.Length != originalSize || file.LastWriteTimeUtc != originalLastWrite)
            sha256 = null;
        command.Parameters.AddWithValue("$browser", download.Browser);
        command.Parameters.AddWithValue("$download_id", download.DownloadId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$path_key", PathKey(path));
        command.Parameters.AddWithValue("$started", download.StartedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$completed", download.CompletedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$size", file.Length);
        command.Parameters.AddWithValue("$last_write", new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToString("O"));
        command.Parameters.AddWithValue("$initial", (object?)initialUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$final", (object?)finalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$referrer", (object?)referrerUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$sha256", (object?)sha256 ?? DBNull.Value);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<StoredDownload> FindByPath(string path)
    {
        if (!DatabaseExists())
        {
            return [];
        }
        using var connection = Open(SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = DatabaseVersion(connection) == 1
            ? """
              SELECT browser, path, started_utc, completed_utc, file_size,
                     file_last_write_utc, initial_url, final_url, referrer_url,
                     NULL AS file_sha256
              FROM downloads WHERE path_key = $path_key ORDER BY completed_utc DESC;
              """
            : """
              SELECT browser, path, started_utc, completed_utc, file_size,
                     file_last_write_utc, initial_url, final_url, referrer_url, file_sha256
              FROM downloads WHERE path_key = $path_key ORDER BY completed_utc DESC;
              """;
        command.Parameters.AddWithValue("$path_key", PathKey(path));
        return ReadDownloads(command);
    }

    public DownloadHistoryPage ReadHistory(
        DownloadHistoryFilter filter, DownloadHistoryCursor? after = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DatabaseExists())
            return new([], null);

        const int pageSize = 50;
        using var connection = Open(SqliteOpenMode.ReadOnly);
        var search = filter.Search.Trim();
        connection.CreateFunction<string, string?, string?, bool>("matches_history", (path, initialUrl, finalUrl) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Path.GetFileName(path).Contains(search, StringComparison.OrdinalIgnoreCase)
                || ProvenanceUrlSanitizer.GetHttpDomain(initialUrl)?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                || ProvenanceUrlSanitizer.GetHttpDomain(finalUrl)?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
        });
        using var command = connection.CreateCommand();
        var hashColumn = DatabaseVersion(connection) == 1 ? "NULL" : "file_sha256";
        var cursorCondition = after is null ? "1" : "(completed_utc, id) < ($completed, $id)";
        var dateCondition = filter.Since is null ? "1" : "completed_utc >= $since";
        command.CommandText = $"""
            SELECT browser, path, started_utc, completed_utc, file_size,
                   file_last_write_utc, initial_url, final_url, referrer_url, {hashColumn}, id
            FROM downloads
            WHERE {cursorCondition}
              AND ($browser IS NULL OR browser = $browser)
              AND {dateCondition}
              AND ($empty_search OR matches_history(path, initial_url, final_url))
            ORDER BY completed_utc DESC, id DESC LIMIT {pageSize + 1};
            """;
        command.Parameters.AddWithValue("$completed", (object?)after?.CompletedUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", (object?)after?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue("$browser", (object?)filter.Browser ?? DBNull.Value);
        command.Parameters.AddWithValue("$since", (object?)filter.Since?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$empty_search", search.Length == 0);

        var entries = new List<DownloadHistoryEntry>(pageSize);
        DownloadHistoryCursor? cursor = null;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count == pageSize)
                return new(entries, cursor);

            var id = reader.GetInt64(10);
            entries.Add(new(id, ReadDownload(reader)));
            cursor = new(reader.GetString(3), id);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(entries, null);
    }

    public bool HasHashedRecord(long size)
    {
        if (!DatabaseExists()) return false;
        using var connection = Open(SqliteOpenMode.ReadOnly);
        if (DatabaseVersion(connection) == 1) return false;
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM downloads
            WHERE file_size = $size AND file_sha256 IS NOT NULL LIMIT 1;
            """;
        command.Parameters.AddWithValue("$size", size);
        return command.ExecuteScalar() is not null;
    }
    public IReadOnlyList<StoredDownload> FindByContent(string path, long size, string sha256)
    {
        if (!DatabaseExists())
        {
            return [];
        }
        using var connection = Open(SqliteOpenMode.ReadOnly);
        if (DatabaseVersion(connection) == 1) return [];
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT browser, path, started_utc, completed_utc, file_size,
                   file_last_write_utc, initial_url, final_url, referrer_url, file_sha256
            FROM downloads
            WHERE file_size = $size AND file_sha256 = $sha256 AND path_key <> $path_key
            ORDER BY completed_utc DESC LIMIT 32;
            """;
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$sha256", sha256);
        command.Parameters.AddWithValue("$path_key", PathKey(path));
        return ReadDownloads(command);
    }

    private static IReadOnlyList<StoredDownload> ReadDownloads(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var results = new List<StoredDownload>();
        while (reader.Read())
            results.Add(ReadDownload(reader));
        return results;
    }

    private static StoredDownload ReadDownload(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1),
        DateTimeOffset.Parse(reader.GetString(2)), DateTimeOffset.Parse(reader.GetString(3)),
        reader.GetInt64(4), DateTimeOffset.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private bool DatabaseExists()
    {
        try
        {
            File.GetAttributes(DatabasePath);
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public int Clear()
    {
        if (!File.Exists(DatabasePath))
        {
            return 0;
        }
        using var connection = Open(SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON; DELETE FROM downloads;";
        var deleted = command.ExecuteNonQuery();
        command.CommandText = "VACUUM;";
        command.ExecuteNonQuery();
        return deleted;
    }

    public static string? TryComputeSha256(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaxHashBytes
                || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;
            var size = file.Length;
            var lastWrite = file.LastWriteTimeUtc;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
            if (stream.Length != size) return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long readTotal = 0;
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                readTotal += read;
                if (readTotal > MaxHashBytes) return null;
                hash.AppendData(buffer.AsSpan(0, read));
            }
            file.Refresh();
            if (!file.Exists || file.Length != size || file.LastWriteTimeUtc != lastWrite)
                return null;
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private SqliteConnection Open(SqliteOpenMode mode)
    {
        var create = mode == SqliteOpenMode.ReadWriteCreate;
        if (create)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        }
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 5
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            var version = DatabaseVersion(connection);
            if (version == 0 && create)
            {
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE downloads (
                      id INTEGER PRIMARY KEY,
                      browser TEXT NOT NULL,
                      download_id TEXT NOT NULL,
                      path TEXT NOT NULL,
                      path_key TEXT NOT NULL,
                      started_utc TEXT NOT NULL,
                      completed_utc TEXT NOT NULL,
                      file_size INTEGER NOT NULL,
                      file_last_write_utc TEXT NOT NULL,
                      initial_url TEXT,
                      final_url TEXT,
                      referrer_url TEXT,
                      file_sha256 TEXT,
                      UNIQUE(browser, download_id, started_utc, path_key)
                    );
                    CREATE INDEX downloads_by_path ON downloads(path_key);
                    CREATE INDEX downloads_by_hash ON downloads(file_size, file_sha256);
                    CREATE INDEX downloads_by_completion ON downloads(completed_utc, id);
                    PRAGMA user_version = 2;
                    """;
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            else if (version == 1 && create)
            {
                using var schema = connection.CreateCommand();
                schema.CommandText = "PRAGMA table_info(downloads);";
                using var reader = schema.ExecuteReader();
                var hasHashColumn = false;
                while (reader.Read())
                    hasHashColumn |= reader.GetString(1) == "file_sha256";
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = (hasHashColumn ? "" : "ALTER TABLE downloads ADD COLUMN file_sha256 TEXT;")
                    + "CREATE INDEX IF NOT EXISTS downloads_by_hash ON downloads(file_size, file_sha256);"
                    + "CREATE INDEX IF NOT EXISTS downloads_by_completion ON downloads(completed_utc, id);"
                    + "PRAGMA user_version = 2;";
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            else if (version is not (1 or 2))
            {
                throw new InvalidDataException($"Unsupported provenance database version: {version}.");
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static int DatabaseVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
    private static string PathKey(string path) => Path.GetFullPath(path).ToUpperInvariant();
}

using Microsoft.Data.Sqlite;
using WhereFrom.Core;
using WhereFrom.Storage;
using Xunit;

namespace WhereFrom.Storage.Tests;

public sealed class DownloadStoreMigrationTests
{
    [Fact]
    public void VersionOneRowsSurviveAtomicMigrationAndNewRowsGetHash()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "download.txt");
            File.WriteAllText(file, "Migration fixture " + Guid.NewGuid().ToString("N"));
            var database = Path.Combine(directory, "records.db");
            var now = DateTimeOffset.UtcNow;
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
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
                      UNIQUE(browser, download_id, started_utc, path_key)
                    );
                    CREATE INDEX downloads_by_path ON downloads(path_key);
                    PRAGMA user_version = 1;
                    """;
                command.ExecuteNonQuery();
                command.CommandText = """
                    INSERT INTO downloads
                    (browser, download_id, path, path_key, started_utc, completed_utc,
                     file_size, file_last_write_utc, initial_url, final_url)
                    VALUES ('edge', 'old', $path, $key, $started, $completed,
                            $size, $last_write, 'https://example.com/old', 'https://example.com/old');
                    """;
                command.Parameters.AddWithValue("$path", file);
                command.Parameters.AddWithValue("$key", Path.GetFullPath(file).ToUpperInvariant());
                command.Parameters.AddWithValue("$started", now.AddSeconds(-2).ToString("O"));
                command.Parameters.AddWithValue("$completed", now.ToString("O"));
                command.Parameters.AddWithValue("$size", new FileInfo(file).Length);
                command.Parameters.AddWithValue("$last_write",
                    new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero).ToString("O"));
                command.ExecuteNonQuery();
            }

            var store = new DownloadStore(database);
            var legacy = Assert.Single(store.FindByPath(file));
            Assert.Null(legacy.FileSha256);
            Assert.Equal("https://example.com/old", legacy.FinalUrl);
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
            Assert.True(store.Save(new BrowserDownload("edge", "new", file,
                now, now.AddSeconds(1), "https://example.com/new?token=private",
                null, null, false)));
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
            }
            var rows = store.FindByPath(file);
            Assert.Equal(2, rows.Count);
            var newest = Assert.Single(rows, row => row.FileSha256 is not null);
            Assert.Equal(64, newest.FileSha256!.Length);
            Assert.Equal("https://example.com/new", newest.InitialUrl);
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 1;";
                command.ExecuteNonQuery();
            }
            Assert.True(store.Save(new BrowserDownload("edge", "retry", file,
                now.AddSeconds(2), now.AddSeconds(3), "https://example.com/retry",
                null, null, false)));
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FailedVersionOneUpgradeLeavesExistingDatabaseIntact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-failed-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "download.txt");
            File.WriteAllText(file, "existing file");
            var database = Path.Combine(directory, "records.db");
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE keep_me (value TEXT); INSERT INTO keep_me VALUES ('preserved'); PRAGMA user_version = 1;";
                command.ExecuteNonQuery();
            }
            var store = new DownloadStore(database);
            var now = DateTimeOffset.UtcNow;
            Assert.Throws<SqliteException>(() => store.Save(new BrowserDownload(
                "edge", "1", file, now, now, "https://example.com/file", null, null, false)));
            using var check = new SqliteConnection($"Data Source={database};Pooling=False");
            check.Open();
            using var query = check.CreateCommand();
            query.CommandText = "PRAGMA user_version;";
            Assert.Equal(1L, Convert.ToInt64(query.ExecuteScalar()));
            query.CommandText = "SELECT value FROM keep_me;";
            Assert.Equal("preserved", query.ExecuteScalar());
            query.CommandText = "PRAGMA quick_check;";
            Assert.Equal("ok", query.ExecuteScalar());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void HashingStopsAtSizeLimit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "large.bin");
            using (var stream = File.Create(file))
                stream.SetLength(DownloadStore.MaxHashBytes + 1);
            Assert.Null(DownloadStore.TryComputeSha256(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

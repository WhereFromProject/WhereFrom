using WhereFrom.Core;
using WhereFrom.Storage;
using Xunit;

namespace WhereFrom.Storage.Tests;

public sealed class DownloadStoreTests
{
    [Fact]
    public void SaveIsPrivatePersistentAndIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "download.txt");
            File.WriteAllText(file, "test");
            var dbPath = Path.Combine(directory, "records.db");
            var store = new DownloadStore(dbPath);
            var start = DateTimeOffset.UtcNow.AddMinutes(-1);
            var item = new BrowserDownload("chrome", "42", file, start, start.AddSeconds(3),
                "https://example.com/file?token=private#part",
                "https://cdn.example.com/file?signature=private",
                "https://example.com/page?session=private", false);

            Assert.True(store.Save(item));
            Assert.False(store.Save(item));
            var found = Assert.Single(new DownloadStore(dbPath).FindByPath(file));
            Assert.Equal("https://example.com/file", found.InitialUrl);
            Assert.Equal("https://cdn.example.com/file", found.FinalUrl);
            Assert.Equal("https://example.com/page", found.ReferrerUrl);
            Assert.DoesNotContain("private", File.ReadAllText(dbPath, System.Text.Encoding.Latin1));
            Assert.Equal(1, store.Clear());
            Assert.Empty(store.FindByPath(file));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IncognitoAndUnusableUrlsAreNotSaved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "sample.txt");
            File.WriteAllText(file, "test");
            var store = new DownloadStore(Path.Combine(directory, "records.db"));
            var now = DateTimeOffset.UtcNow;
            Assert.False(store.Save(new BrowserDownload("edge", "1", file, now, now,
                "https://example.com/?token=private", null, null, true)));
            Assert.False(store.Save(new BrowserDownload("edge", "2", file, now, now,
                "file:///C:/secret", null, null, false)));
            Assert.False(File.Exists(store.DatabasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptDatabaseIsReported()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wherefrom-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "records.db");
            File.WriteAllText(database, "not a database");
            Assert.ThrowsAny<Exception>(() => new DownloadStore(database).FindByPath("C:\\sample.txt"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

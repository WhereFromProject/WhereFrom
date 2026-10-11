using System.Text.Json;
using Microsoft.Data.Sqlite;
using WhereFrom.Cli;
using WhereFrom.Core;
using WhereFrom.Storage;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public sealed class FileMoveQueryTests
{
    [Fact]
    public void MovedAndCopiedContentIsOnlyAPossibleAssociation()
    {
        using var fixture = new Fixture();
        fixture.Save();
        var moved = Path.Combine(fixture.DirectoryPath, "移动 后的文件.txt");
        File.Move(fixture.OriginalPath, moved);
        var movedResult = fixture.Query(moved);
        Assert.Equal(0, movedResult.Code);
        using (var json = JsonDocument.Parse(movedResult.Output))
        {
            var root = json.RootElement;
            Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceUrl").ValueKind);
            Assert.Equal("possible", root.GetProperty("completeness").GetString());
            Assert.Equal(1, root.GetProperty("possibleBrowserRecords").GetInt32());
            var evidence = Assert.Single(root.GetProperty("evidence").EnumerateArray());
            Assert.Equal("possible", evidence.GetProperty("match").GetString());
            Assert.Equal("sha256", evidence.GetProperty("matchBasis").GetString());
            Assert.Equal("https://example.com/file", evidence.GetProperty("sourceUrl").GetString());
        }
        Assert.Contains("Possible edge download association", fixture.QueryText(moved));
        var copied = Path.Combine(fixture.DirectoryPath, "复制 后的文件.txt");
        File.Copy(moved, copied);
        Assert.Equal("possible", fixture.Completeness(copied));
        Assert.Equal(1, fixture.Open(copied));
    }

    [Fact]
    public void SamePathReplacementWithSameSizeAndTimestampDoesNotInheritDownload()
    {
        using var fixture = new Fixture();
        fixture.Save();
        var lastWrite = File.GetLastWriteTimeUtc(fixture.OriginalPath);
        var oldSize = new FileInfo(fixture.OriginalPath).Length;
        File.Delete(fixture.OriginalPath);
        File.WriteAllText(fixture.OriginalPath, new string('X', (int)oldSize));
        File.SetLastWriteTimeUtc(fixture.OriginalPath, lastWrite);
        var result = fixture.Query(fixture.OriginalPath);
        Assert.Equal(1, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceUrl").ValueKind);
        Assert.Equal(1, root.GetProperty("historicalBrowserRecords").GetInt32());
        Assert.Equal(0, root.GetProperty("possibleBrowserRecords").GetInt32());
    }

    [Fact]
    public void LegacyMetadataOnlyRecordStaysPossibleWithoutOpeningUrl()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE downloads SET file_sha256 = NULL; PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }
        var result = fixture.Query(fixture.OriginalPath);
        Assert.Equal(0, result.Code);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("sourceUrl").ValueKind);
        Assert.Equal("possible", json.RootElement.GetProperty("completeness").GetString());
        var evidence = Assert.Single(json.RootElement.GetProperty("evidence").EnumerateArray());
        Assert.Equal("metadata", evidence.GetProperty("matchBasis").GetString());
        Assert.Equal(1, fixture.Open(fixture.OriginalPath));
        Assert.Contains("content unverified", fixture.QueryText(fixture.OriginalPath));
        using var check = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        check.Open();
        using var version = check.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, Convert.ToInt64(version.ExecuteScalar()));
    }
    [Fact]
    public void DirectoryAuditSeparatesPossibleFromUnknownAndRespectsLimit()
    {
        using var fixture = new Fixture();
        fixture.Save();
        var auditDirectory = Path.Combine(fixture.DirectoryPath, "audit");
        Directory.CreateDirectory(auditDirectory);
        File.Move(fixture.OriginalPath, Path.Combine(auditDirectory, "移动 文件.txt"));
        File.WriteAllText(Path.Combine(auditDirectory, "未知 文件.txt"), Guid.NewGuid().ToString("N"));
        var report = fixture.Audit(auditDirectory, 2);
        Assert.Equal(0, report.Code);
        Assert.Contains("Possible associations: 1", report.Output);
        Assert.Contains("Unknown: 1", report.Output);
        Assert.Contains("Limit reached: no", report.Output);
        var limited = fixture.Audit(auditDirectory, 1);
        Assert.Equal(0, limited.Code);
        Assert.Contains("Scanned: 1", limited.Output);
        Assert.Contains("Limit reached: yes", limited.Output);
    }
    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(
            AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        public string OriginalPath => Path.Combine(DirectoryPath, "原始 文件.txt");
        public string DatabasePath => Path.Combine(DirectoryPath, "records.db");
        private DownloadStore Store => new(DatabasePath);

        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(OriginalPath, "Unique file " + Guid.NewGuid().ToString("N"));
        }

        public void Save()
        {
            var now = DateTimeOffset.UtcNow;
            Assert.True(Store.Save(new BrowserDownload("edge", "42", OriginalPath,
                now.AddSeconds(-2), now, "https://example.com/file?token=private",
                "https://example.com/file?token=private", null, false)));
        }

        public (int Code, string Output) Query(string path)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = CommandLine.Run([path, "--json"], output, error, downloadStore: Store);
            return (code, output.ToString());
        }

        public string Completeness(string path)
        {
            using var json = JsonDocument.Parse(Query(path).Output);
            return json.RootElement.GetProperty("completeness").GetString()!;
        }

        public string QueryText(string path)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CommandLine.Run([path], output, error, downloadStore: Store));
            return output.ToString();
        }

        public (int Code, string Output) Audit(string path, int limit)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = CommandLine.Run(["audit", path, "--limit", limit.ToString()],
                output, error, downloadStore: Store);
            return (code, output.ToString());
        }
        public int Open(string path)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var launched = false;
            var code = CommandLine.Run(["open", path], output, error,
                launch: _ => launched = true, downloadStore: Store);
            Assert.False(launched);
            return code;
        }

        public void Dispose() => System.IO.Directory.Delete(DirectoryPath, recursive: true);
    }
}

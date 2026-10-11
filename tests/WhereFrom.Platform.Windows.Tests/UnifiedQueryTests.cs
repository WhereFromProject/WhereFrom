using System.Text.Json;
using WhereFrom.Cli;
using WhereFrom.Core;
using WhereFrom.Storage;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public sealed class UnifiedQueryTests
{
    [Fact]
    public void BrowserOnlyDownloadIsQueryableOfflineWithSanitizedUrl()
    {
        using var fixture = new Fixture();
        fixture.SaveBrowserDownload("https://example.com/start?secret=1",
            "https://cdn.example.com/file?token=private#fragment");
        var (code, json, error) = fixture.Query(json: true);
        Assert.Equal(0, code);
        Assert.Equal("", error);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("hasProvenance").GetBoolean());
        Assert.Equal("sourceUrl", root.GetProperty("completeness").GetString());
        Assert.Equal("https://cdn.example.com/file", root.GetProperty("sourceUrl").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("zone").ValueKind);
        Assert.Equal("browser", root.GetProperty("evidence")[0].GetProperty("type").GetString());
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("secret", json);
        var text = fixture.Query(json: false);
        Assert.Contains("chrome download", text.Output);
        Assert.Contains("https://cdn.example.com/file", text.Output);
        Assert.Equal("https://cdn.example.com/file", fixture.SelectedOpenUrl());
    }

    [Fact]
    public void ConflictingWindowsAndBrowserEvidenceAreBothShown()
    {
        using var fixture = new Fixture();
        fixture.WriteZone("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://windows.example/file?raw=1");
        fixture.SaveBrowserDownload("https://browser.example/file?secret=1",
            "https://browser.example/file?secret=2");
        var (code, json, _) = fixture.Query(json: true);
        Assert.Equal(0, code);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("conflict").GetBoolean());
        Assert.Equal("https://windows.example/file?raw=1", root.GetProperty("sourceUrl").GetString());
        Assert.Equal(2, root.GetProperty("evidence").GetArrayLength());
        Assert.Equal("https://browser.example/file",
            root.GetProperty("evidence")[1].GetProperty("sourceUrl").GetString());
        var text = fixture.Query(json: false);
        Assert.Contains("Windows and browser source URLs differ", text.Output);
        Assert.Contains("https://windows.example/file?raw=1", text.Output);
        Assert.Contains("https://browser.example/file", text.Output);
    }

    [Fact]
    public void ReplacedFileDoesNotInheritOldBrowserSource()
    {
        using var fixture = new Fixture();
        fixture.SaveBrowserDownload("https://example.com/old", "https://example.com/old");
        File.WriteAllText(fixture.FilePath, "A replacement with another size.");
        var (code, json, _) = fixture.Query(json: true);
        Assert.Equal(1, code);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceUrl").ValueKind);
        Assert.False(root.GetProperty("hasProvenance").GetBoolean());
        Assert.Equal(1, root.GetProperty("historicalBrowserRecords").GetInt32());
        Assert.Empty(root.GetProperty("evidence").EnumerateArray());
    }

    [Fact]
    public void ZoneOnlyUnknownAndBrokenStoreAreDistinct()
    {
        using var fixture = new Fixture();
        var unknown = fixture.Query(json: true);
        Assert.Equal(1, unknown.Code);
        using (var document = JsonDocument.Parse(unknown.Output))
            Assert.Equal("unknown", document.RootElement.GetProperty("completeness").GetString());
        fixture.WriteZone("[ZoneTransfer]\r\nZoneId=3");
        var zoneOnly = fixture.Query(json: true);
        Assert.Equal(0, zoneOnly.Code);
        using (var document = JsonDocument.Parse(zoneOnly.Output))
            Assert.Equal("zoneOnly", document.RootElement.GetProperty("completeness").GetString());
        File.WriteAllText(fixture.DatabasePath, "not a SQLite database");
        var broken = fixture.Query(json: true);
        Assert.Equal(0, broken.Code);
        Assert.Contains("Browser records unavailable.", broken.Error);
        using var brokenJson = JsonDocument.Parse(broken.Output);
        Assert.Equal("unavailable", brokenJson.RootElement.GetProperty("browserStoreStatus").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(directory, "download.txt");
        public string DatabasePath => Path.Combine(directory, "records.db");
        private DownloadStore Store => new(DatabasePath);

        public Fixture()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, "Original download body.");
        }

        public void WriteZone(string content) => File.WriteAllText(FilePath + ":Zone.Identifier", content);

        public void SaveBrowserDownload(string initial, string final)
        {
            var start = DateTimeOffset.UtcNow.AddSeconds(-5);
            Assert.True(Store.Save(new BrowserDownload("chrome", "1", FilePath, start,
                start.AddSeconds(3), initial, final, null, false)));
        }

        public (int Code, string Output, string Error) Query(bool json)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var arguments = json ? new[] { FilePath, "--json" } : new[] { FilePath };
            var code = CommandLine.Run(arguments, output, error,
                downloadStore: Store);
            return (code, output.ToString(), error.ToString());
        }

        public string? SelectedOpenUrl()
        {
            string? selected = null;
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CommandLine.Run(["open", FilePath], output, error,
                launch: info => selected = info.FileName, downloadStore: Store));
            return selected;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}

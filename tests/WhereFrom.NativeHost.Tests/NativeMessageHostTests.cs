using System.Buffers.Binary;
using System.Text.Json;
using WhereFrom.NativeHost;
using WhereFrom.Storage;
using Xunit;

namespace WhereFrom.NativeHost.Tests;

public sealed class NativeMessageHostTests
{
    private const string ChromeId = "abcdefghijklmnopabcdefghijklmnop";
    private const string EdgeId = "ponmlkjihgfedcbaponmlkjihgfedcba";

    [Fact]
    public void ValidCompletedDownloadIsSavedAndDuplicateIsIgnored()
    {
        using var fixture = new Fixture();
        var message = fixture.ValidMessage;
        var replies = fixture.Run(ChromeId, Frame(message).Concat(Frame(message)).ToArray());
        Assert.Equal(new[] { "saved", "ignored" }, replies);
        Assert.Single(new DownloadStore(fixture.DatabasePath).FindByPath(fixture.FilePath));
    }

    [Fact]
    public void UnknownOriginAndWrongBrowserAreRejected()
    {
        using var fixture = new Fixture();
        Assert.Equal(new[] { "origin_not_allowed" }, fixture.Run(new string('a', 32), Frame(fixture.ValidMessage)));
        Assert.Equal(new[] { "invalid_message" }, fixture.Run(EdgeId, Frame(fixture.ValidMessage)));
        Assert.False(File.Exists(fixture.DatabasePath));
    }

    [Fact]
    public void SameExtensionIdCanBeRegisteredForBothBrowsers()
    {
        using var fixture = new Fixture();
        fixture.UseSameIdForBothBrowsers();
        var edgeMessage = fixture.ValidMessage.Replace("\"browser\":\"chrome\"", "\"browser\":\"edge\"");
        Assert.Equal(new[] { "saved" }, fixture.Run(ChromeId, Frame(edgeMessage)));
        Assert.Equal("edge", Assert.Single(new DownloadStore(fixture.DatabasePath).FindByPath(fixture.FilePath)).Browser);
    }

    [Fact]
    public void InvalidVersionMalformedAndOversizedMessagesAreRejected()
    {
        using var fixture = new Fixture();
        var wrongVersion = fixture.ValidMessage.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
        Assert.Equal(new[] { "invalid_message", "invalid_message" },
            fixture.Run(ChromeId, Frame(wrongVersion).Concat(Frame("{" )).ToArray()));
        var oversized = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(oversized, NativeMessageHost.MaxMessageBytes + 1);
        Assert.Equal(new[] { "invalid_length" }, fixture.Run(ChromeId, oversized));
        Assert.False(File.Exists(fixture.DatabasePath));
    }

    private static byte[] Frame(string json)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(json);
        var framed = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(framed, (uint)payload.Length);
        payload.CopyTo(framed, 4);
        return framed;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "wherefrom-host-" + Guid.NewGuid().ToString("N"));
        public string FilePath => Path.Combine(directory, "sample.txt");
        public string DatabasePath => Path.Combine(directory, "records.db");
        private string ConfigPath => Path.Combine(directory, "allowlist.json");
        public string ValidMessage { get; }

        public void UseSameIdForBothBrowsers() =>
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new { chrome = ChromeId, edge = ChromeId }));

        public Fixture()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, "hello");
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new { chrome = ChromeId, edge = EdgeId }));
            ValidMessage = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                @event = "download.completed",
                download = new
                {
                    browser = "chrome", downloadId = "123", path = FilePath,
                    startedAt = DateTimeOffset.UtcNow.AddSeconds(-3), completedAt = DateTimeOffset.UtcNow,
                    initialUrl = "https://example.com/first?secret=1",
                    finalUrl = "https://example.com/final?secret=2",
                    referrerUrl = "https://example.com/page#private", incognito = false
                }
            });
        }

        public string[] Run(string id, byte[] request)
        {
            using var input = new MemoryStream(request);
            using var output = new MemoryStream();
            new NativeMessageHost().Run(input, output, $"chrome-extension://{id}/", ConfigPath, DatabasePath);
            output.Position = 0;
            var replies = new List<string>();
            var prefix = new byte[4];
            while (output.Position < output.Length)
            {
                output.ReadExactly(prefix);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
                var payload = new byte[length];
                output.ReadExactly(payload);
                using var document = JsonDocument.Parse(payload);
                replies.Add(document.RootElement.GetProperty("Code").GetString()!);
            }
            return [.. replies];
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}

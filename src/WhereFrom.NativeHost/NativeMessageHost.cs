using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WhereFrom.Core;
using WhereFrom.Storage;

namespace WhereFrom.NativeHost;

public sealed class NativeMessageHost
{
    public const int MaxMessageBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public void Run(Stream input, Stream output, string? origin, string? configPath = null,
        string? databasePath = null)
    {
        var allowed = LoadAllowedOrigins(configPath ?? DefaultConfigPath());
        if (origin is null || !allowed.TryGetValue(origin, out var browsers))
        {
            Write(output, new HostReply(false, "origin_not_allowed"));
            return;
        }

        Span<byte> prefix = stackalloc byte[4];
        while (true)
        {
            var count = ReadPrefix(input, prefix);
            if (count == 0) return;
            if (count != 4)
            {
                Write(output, new HostReply(false, "invalid_length"));
                return;
            }
            var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            if (length is 0 or > MaxMessageBytes)
            {
                Write(output, new HostReply(false, "invalid_length"));
                return;
            }

            var bytes = new byte[length];
            try
            {
                input.ReadExactly(bytes);
                var message = JsonSerializer.Deserialize<DownloadMessage>(bytes, JsonOptions);
                if (message is null || message.SchemaVersion != 1 || message.Event != "download.completed"
                    || message.Download is null || message.Download.Browser is null
                    || !browsers.Contains(message.Download.Browser))
                {
                    Write(output, new HostReply(false, "invalid_message"));
                    continue;
                }
                var download = message.Download;
                var saved = new DownloadStore(databasePath ?? DownloadStore.DefaultPath).Save(
                    new BrowserDownload(download.Browser!, download.DownloadId ?? "", download.Path ?? "",
                        download.StartedAt, download.CompletedAt, download.InitialUrl,
                        download.FinalUrl, download.ReferrerUrl, download.Incognito));
                Write(output, new HostReply(true, saved ? "saved" : "ignored"));
            }
            catch (Exception error) when (error is EndOfStreamException or JsonException or ArgumentException
                or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                Write(output, new HostReply(false, error switch
                {
                    EndOfStreamException => "truncated_message",
                    JsonException or ArgumentException => "invalid_message",
                    FileNotFoundException => "file_not_found",
                    _ => "storage_unavailable"
                }));
                if (error is EndOfStreamException) return;
            }
        }
    }

    public static string DefaultConfigPath()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "native-host-allowlist.json");
        if (File.Exists(installed)) return installed;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhereFrom", "native-host-allowlist.json");
    }

    private static Dictionary<string, HashSet<string>> LoadAllowedOrigins(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var browser in new[] { "chrome", "edge" })
            {
                if (!document.RootElement.TryGetProperty(browser, out var idElement)) continue;
                var id = idElement.GetString();
                if (id is { Length: 32 } && id.All(c => c is >= 'a' and <= 'p'))
                {
                    var origin = $"chrome-extension://{id}/";
                    if (!result.TryGetValue(origin, out var browsers))
                        result[origin] = browsers = new HashSet<string>(StringComparer.Ordinal);
                    browsers.Add(browser);
                }
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return [];
        }
    }

    private static int ReadPrefix(Stream input, Span<byte> prefix)
    {
        var read = 0;
        while (read < prefix.Length)
        {
            var count = input.Read(prefix[read..]);
            if (count == 0) break;
            read += count;
        }
        return read;
    }

    private static void Write(Stream output, HostReply reply)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(reply, JsonOptions);
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)payload.Length);
        output.Write(prefix);
        output.Write(payload);
        output.Flush();
    }

    private sealed record DownloadMessage(int SchemaVersion, string? Event, IncomingDownload? Download);
    private sealed record IncomingDownload(string? Browser, string? DownloadId, string? Path,
        DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string? InitialUrl,
        string? FinalUrl, string? ReferrerUrl, bool Incognito);
    private sealed record HostReply(bool Ok, string Code);
}

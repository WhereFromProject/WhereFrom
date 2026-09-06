using System.Text.Json;
using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class JsonCommandTests
{
    [Fact]
    public void CompleteRealMetadataRoundTripsWithoutChangingEvidence()
    {
        using var file = new TemporaryZoneFile("下载 😀 & 文件.zip");
        const string source = "https://example.com/下载/😀?name=%22a%5Cb%22&token=a%2Fb#部分";
        const string referrer = "https://github.com/owner/repo/releases";
        file.WriteZone($"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl={source}\r\nReferrerUrl={referrer}\0");
        var body = File.ReadAllBytes(file.Path);
        var metadata = File.ReadAllBytes(file.StreamPath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run([file.Path, "--json"], output, error));

        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.Equal(
            ["schemaVersion", "path", "hasProvenance", "zone", "sourceUrl", "referrerUrl"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(file.Path, root.GetProperty("path").GetString());
        Assert.True(root.GetProperty("hasProvenance").GetBoolean());
        var zone = root.GetProperty("zone");
        Assert.Equal(["id", "name"], zone.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(3, zone.GetProperty("id").GetInt32());
        Assert.Equal("Internet", zone.GetProperty("name").GetString());
        Assert.Equal(source, root.GetProperty("sourceUrl").GetString());
        Assert.Equal(referrer, root.GetProperty("referrerUrl").GetString());
        Assert.Equal("", error.ToString());
        Assert.Equal(body, File.ReadAllBytes(file.Path));
        Assert.Equal(metadata, File.ReadAllBytes(file.StreamPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[ZoneTransfer]\r\nHostUrl=invalid")]
    public void NoUsableProvenanceHasExplicitNulls(string? metadata)
    {
        using var file = new TemporaryZoneFile();
        if (metadata is not null)
        {
            file.WriteZone(metadata);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, CommandLine.Run([file.Path, "--json"], output, error));

        using var json = JsonDocument.Parse(output.ToString());
        var root = json.RootElement;
        Assert.False(root.GetProperty("hasProvenance").GetBoolean());
        foreach (var field in new[] { "zone", "sourceUrl", "referrerUrl" })
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty(field).ValueKind);
        }
        Assert.Equal(metadata?.Contains("invalid") == true, error.ToString().Length > 0);
        if (metadata is null)
        {
            Assert.Throws<FileNotFoundException>(() => File.ReadAllBytes(file.StreamPath));
        }
    }

    [Theory]
    [InlineData("ZoneId=3", "zone")]
    [InlineData("HostUrl=https://example.com/file", "sourceUrl")]
    [InlineData("ReferrerUrl=https://example.com/page", "referrerUrl")]
    public void PartialProvenanceKeepsOnlyAvailableValues(string metadata, string present)
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\r\n" + metadata);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run([file.Path, "--json"], output, error));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("hasProvenance").GetBoolean());
        foreach (var field in new[] { "zone", "sourceUrl", "referrerUrl" })
        {
            Assert.Equal(field != present, json.RootElement.GetProperty(field).ValueKind == JsonValueKind.Null);
        }
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void UnknownZoneAndBrokenUrlKeepJsonParseableAndWarningSeparate()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\r\nZoneId=42\r\nHostUrl=invalid");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run([file.Path, "--json"], output, error));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(42, json.RootElement.GetProperty("zone").GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("zone").GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("sourceUrl").ValueKind);
        Assert.Equal("Some provenance metadata was incomplete, invalid, or duplicated." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void JsonEscapingRoundTripsQuotesBackslashesAndControls()
    {
        // DTO escaping is tested independently of which characters Windows paths/URLs allow.
        const string value = "quote\" slash\\ newline\r\n tab\t nul\0 escape\u001b 中文😀";
        var result = new ProvenanceResult(value, "internal-provider", ProvenanceReadStatus.Read)
        {
            SourceUrl = value,
            ReferrerUrl = value
        };

        var text = ProvenanceJson.Serialize(result);

        using var json = JsonDocument.Parse(text);
        Assert.Equal(value, json.RootElement.GetProperty("path").GetString());
        Assert.Equal(value, json.RootElement.GetProperty("sourceUrl").GetString());
        Assert.Equal(value, json.RootElement.GetProperty("referrerUrl").GetString());
        Assert.DoesNotContain("internal-provider", text);
        Assert.DoesNotContain('\0', text);
        Assert.DoesNotContain('\u001b', text);
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.FileNotFound, 3)]
    [InlineData(ProvenanceReadStatus.AccessDenied, 3)]
    [InlineData(ProvenanceReadStatus.InvalidPath, 2)]
    [InlineData(ProvenanceReadStatus.NotAFile, 2)]
    [InlineData(ProvenanceReadStatus.ReadFailed, 4)]
    public void ErrorsMatchTextModeAndProduceNoJson(ProvenanceReadStatus status, int expected)
    {
        var provider = new StubProvider(path => new(path, "test", status));
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var textOutput = new StringWriter();
        using var textError = new StringWriter();

        Assert.Equal(expected, CommandLine.Run(["file.txt", "--json"], output, error, provider));
        Assert.Equal(expected, CommandLine.Run(["file.txt"], textOutput, textError, provider));
        Assert.Equal("", output.ToString());
        Assert.NotEmpty(error.ToString());
        Assert.Equal(textError.ToString(), error.ToString());
    }

    [Fact]
    public void UnexpectedFailureDoesNotLeakExceptionDetails()
    {
        var provider = new StubProvider(_ => throw new InvalidOperationException("private detail"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(5, CommandLine.Run(["file.txt", "--json"], output, error, provider));
        Assert.Equal("", output.ToString());
        Assert.Equal("An unexpected error occurred while reading provenance information." + Environment.NewLine, error.ToString());
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--json", "file.txt")]
    [InlineData("file.txt", "--JSON")]
    [InlineData("file.txt", "--json", "--json")]
    [InlineData("debug-zone", "file.txt", "--json")]
    [InlineData("--help", "--json")]
    public void UnsupportedJsonSyntaxDoesNotInspectAFile(params string[] args)
    {
        var provider = new StubProvider(_ => throw new InvalidOperationException("Must not inspect"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CommandLine.Run(args, output, error, provider));
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void MissingRealFileAndDirectoryRemainErrors()
    {
        using var file = new TemporaryZoneFile();
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(3, CommandLine.Run([file.Path + ".missing", "--json"], output, error));
        Assert.Equal(2, CommandLine.Run([AppContext.BaseDirectory, "--json"], output, error));
        Assert.Equal("", output.ToString());
    }

    private sealed class StubProvider(Func<string, ProvenanceResult> inspect) : IProvenanceProvider
    {
        public ProvenanceResult Inspect(string path) => inspect(path);
    }
}

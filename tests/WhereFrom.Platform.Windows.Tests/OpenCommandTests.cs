using System.ComponentModel;
using System.Diagnostics;
using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class OpenCommandTests
{
    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com/page")]
    [InlineData("HTTPS://example.com/page")]
    [InlineData("https://例子.测试/下载/😀?name=%22a%5Cb%22&token=a%2Fb#部分")]
    [InlineData("https://example.com/?a=1&b=$(test);x='value'")]
    public void OpensExactReferrerWithoutCommandArguments(string url)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        ProcessStartInfo? launched = null;
        var provider = Provider(url, "https://cdn.example/file");
        Assert.Equal(0, CommandLine.Run(["open", "file.zip"], output, error, provider, value => launched = value));
        Assert.NotNull(launched);
        Assert.Equal(url, launched.FileName);
        Assert.True(launched.UseShellExecute);
        Assert.Equal("open", launched.Verb);
        Assert.Equal("", launched.Arguments);
        Assert.Empty(launched.ArgumentList);
        Assert.Equal("Opening:" + Environment.NewLine + url + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void FallsBackToSourceOnlyWhenReferrerIsMissing()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        string? opened = null;
        Assert.Equal(0, CommandLine.Run(["open", "file.zip"], output, error,
            Provider(null, "https://cdn.example/file"), info => opened = info.FileName));
        Assert.Equal("https://cdn.example/file", opened);
    }

    [Theory]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("shell:AppsFolder")]
    [InlineData("custom:hello")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a URI")]
    [InlineData("//example.com/path")]
    [InlineData("https:example.com")]
    [InlineData("https:///example.com")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/a\\b")]
    [InlineData("https://example.com/\"")]
    [InlineData("https://example.com/%ZZ")]
    [InlineData("https://example.com/\n")]
    [InlineData("https://example.com/\0")]
    public void RefusesSelectedInvalidUrlWithoutFallbackOrLaunch(string url)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var called = false;
        Assert.Equal(2, CommandLine.Run(["open", "file.zip"], output, error,
            Provider(url, "https://safe.example/file"), _ => called = true));
        Assert.False(called);
        Assert.Equal("", output.ToString());
        Assert.Contains("Only valid HTTP or HTTPS", error.ToString());
        Assert.Throws<ArgumentException>(() => BrowserLauncher.CreateStartInfo(url));
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.Read)]
    [InlineData(ProvenanceReadStatus.NoMetadata)]
    public void NoUrlDoesNotLaunch(ProvenanceReadStatus status)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var called = false;
        var provider = new StubProvider(path => new(path, "test", status) { Zone = new(3, "Internet") });
        Assert.Equal(1, CommandLine.Run(["open", "file.zip"], output, error, provider, _ => called = true));
        Assert.False(called);
        Assert.Equal("No source URL is available for this file." + Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.FileNotFound, 3)]
    [InlineData(ProvenanceReadStatus.NotAFile, 2)]
    [InlineData(ProvenanceReadStatus.AccessDenied, 3)]
    [InlineData(ProvenanceReadStatus.ReadFailed, 4)]
    [InlineData(ProvenanceReadStatus.InvalidPath, 2)]
    public void ReadErrorsNeverLaunch(ProvenanceReadStatus status, int expected)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var called = false;
        var provider = new StubProvider(path => new(path, "test", status) { SourceUrl = "https://example.com" });
        Assert.Equal(expected, CommandLine.Run(["open", "file.zip"], output, error, provider, _ => called = true));
        Assert.False(called);
        Assert.Equal("", output.ToString());
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void BrowserFailureReportsFailureWithoutExceptionDetails()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(4, CommandLine.Run(["open", "file.zip"], output, error,
            Provider(null, "https://example.com"), _ => throw new Win32Exception("secret")));
        Assert.Contains("Unable to open", error.ToString());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public void UnexpectedFailureIsContained()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(5, CommandLine.Run(["open", "file.zip"], output, error,
            new StubProvider(_ => throw new Exception("secret")), _ => Assert.Fail("Must not launch")));
        Assert.Equal("", output.ToString());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("[ZoneTransfer]\nZoneId=3\nHostUrl=https://cdn.example/file\nReferrerUrl=https://github.com/repo/releases\0", "https://github.com/repo/releases", 0)]
    [InlineData("[ZoneTransfer]\nHostUrl=https://example.com/file", "https://example.com/file", 0)]
    [InlineData("[ZoneTransfer]\nReferrerUrl=file:///C:/test.txt\nHostUrl=https://example.com/file", null, 2)]
    [InlineData("[ZoneTransfer]\nHostUrl=javascript:alert(1)", null, 2)]
    [InlineData("[ZoneTransfer]\nHostUrl=invalid", null, 1)]
    public void RealAdsSelectionPreservesBodyAndMetadata(string metadata, string? expected, int code)
    {
        using var file = new TemporaryZoneFile("来源😀.zip");
        file.WriteZone(metadata);
        var body = File.ReadAllBytes(file.Path);
        var ads = File.ReadAllBytes(file.StreamPath);
        using var output = new StringWriter();
        using var error = new StringWriter();
        string? opened = null;
        Assert.Equal(code, CommandLine.Run(["open", file.Path], output, error, launch: info => opened = info.FileName));
        Assert.Equal(expected, opened);
        Assert.Equal(body, File.ReadAllBytes(file.Path));
        Assert.Equal(ads, File.ReadAllBytes(file.StreamPath));
        if (metadata.Contains("invalid"))
        {
            Assert.Contains("invalid", error.ToString());
        }
    }

    [Theory]
    [InlineData("open", "file.zip", "--json")]
    [InlineData("open", "file.zip", "--unknown")]
    [InlineData("open", "--help")]
    public void UnsupportedOptionsDoNotInspectOrLaunch(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CommandLine.Run(args, output, error,
            new StubProvider(_ => throw new Exception("Must not inspect")), _ => Assert.Fail("Must not launch")));
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BareOpenFilenameRetainsSingleFileBehavior(bool json)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = json ? ["open", "--json"] : ["open"];
        Assert.Equal(0, CommandLine.Run(args, output, error, Provider(null, "https://example.com"),
            _ => Assert.Fail("Single-file query must not launch")));
        Assert.DoesNotContain("Opening:", output.ToString());
    }

    private static IProvenanceProvider Provider(string? referrer, string? source) =>
        new StubProvider(path => new(path, "test", ProvenanceReadStatus.Read)
        {
            ReferrerUrl = referrer,
            SourceUrl = source
        });

    private sealed class StubProvider(Func<string, ProvenanceResult> inspect) : IProvenanceProvider
    {
        public ProvenanceResult Inspect(string path) => inspect(path);
    }
}

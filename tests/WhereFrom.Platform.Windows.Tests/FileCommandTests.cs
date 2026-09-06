using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class FileCommandTests
{
    [Fact]
    public void RealAdsFlowsThroughProviderAndFormatter()
    {
        using var file = new TemporaryZoneFile("下载文件😀.zip");
        file.WriteZone(
            "[ZoneTransfer]\r\n"
            + "ZoneId=3\r\n"
            + "ReferrerUrl=https://github.com/owner/repo/releases\r\n"
            + "HostUrl=https://codeload.github.com/owner/repo/zip/v1\0");
        var body = File.ReadAllBytes(file.Path);
        var metadata = File.ReadAllBytes(file.StreamPath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run([file.Path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("下载文件😀.zip", output.ToString());
        Assert.Contains("https://codeload.github.com/owner/repo/zip/v1", output.ToString());
        Assert.Contains("https://github.com/owner/repo/releases", output.ToString());
        Assert.Contains("Internet (3)", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Equal(body, File.ReadAllBytes(file.Path));
        Assert.Equal(metadata, File.ReadAllBytes(file.StreamPath));
    }

    [Fact]
    public void LocalFileWithoutMetadataReturnsUnknown()
    {
        using var file = new TemporaryZoneFile("本地文件.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run([file.Path], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("本地文件.txt", output.ToString());
        Assert.Contains("No provenance information found.", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(file.StreamPath));
    }

    [Fact]
    public void MissingFileReturnsInputError()
    {
        using var file = new TemporaryZoneFile();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run([file.Path + ".missing"], output, error);

        Assert.Equal(3, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Equal("File not found." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void InjectedProviderReceivesUnicodePath()
    {
        const string path = @"C:\下载\文件 😀.pdf";
        string? received = null;
        var provider = new StubProvider(value =>
        {
            received = value;
            return new(value, "test", ProvenanceReadStatus.Read)
            {
                SourceUrl = "https://例子.测试/下载"
            };
        });
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run([path], output, error, provider);

        Assert.Equal(0, exitCode);
        Assert.Equal(path, received);
        Assert.Contains("文件 😀.pdf", output.ToString());
        Assert.Contains("https://例子.测试/下载", output.ToString());
    }

    [Fact]
    public void HelpListsOnlyCurrentCommands()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("wherefrom <file>", output.ToString());
        Assert.Contains("wherefrom --version", output.ToString());
        Assert.Contains("debug-zone", output.ToString());
        Assert.Contains("wherefrom <file> --json", output.ToString());
        Assert.Contains("wherefrom scan <directory>", output.ToString());
        Assert.DoesNotContain("open <file>", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData()]
    [InlineData("--json")]
    [InlineData("one", "two")]
    public void InvalidArgumentsReturnUsage(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run(args, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void UnexpectedProviderExceptionDoesNotLeakAStackTrace()
    {
        var provider = new StubProvider(_ => throw new InvalidOperationException("private detail"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CommandLine.Run(["file.txt"], output, error, provider);

        Assert.Equal(5, exitCode);
        Assert.Equal("", output.ToString());
        Assert.Equal(
            "An unexpected error occurred while reading provenance information." + Environment.NewLine,
            error.ToString());
        Assert.DoesNotContain("private detail", error.ToString());
        Assert.DoesNotContain("InvalidOperationException", error.ToString());
    }

    private sealed class StubProvider(Func<string, ProvenanceResult> inspect) : IProvenanceProvider
    {
        public ProvenanceResult Inspect(string path) => inspect(path);
    }

    [Theory]
    [InlineData("[ZoneTransfer]\nZoneId=3", 0, "No source URL recorded.")]
    [InlineData("[ZoneTransfer]\nReferrerUrl=https://example.com/page", 0, "Referrer")]
    [InlineData("[ZoneTransfer]\nHostUrl=https://example.com/file", 0, "Source")]
    [InlineData("", 1, "No provenance information found.")]
    public void PartialAndEmptyRealStreamsReachTheUser(string text, int code, string message)
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone(text);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(code, CommandLine.Run([file.Path], output, error));
        Assert.Contains(message, output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void DirectoryInputIsReportedWithoutCrashing()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CommandLine.Run([AppContext.BaseDirectory], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Path is a directory", error.ToString());
    }

    [Fact]
    public void ReservedWordCanStillBeAFileWithAnExplicitPath()
    {
        using var file = new TemporaryZoneFile("debug-zone");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(1, CommandLine.Run([file.Path], output, error));
        Assert.Contains("No provenance information found.", output.ToString());
    }

    [Fact]
    public void BrokenSourceKeepsZoneAndReportsMetadataProblem()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\nZoneId=3\nHostUrl=not a URL");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run([file.Path], output, error));
        Assert.Contains("Internet (3)", output.ToString());
        Assert.Contains("invalid", error.ToString());
    }
}

using WhereFrom.Cli;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class DebugZoneCommandTests
{
    [Fact]
    public void PrintsRawContentAndPreservesUrls()
    {
        using var file = new TemporaryZoneFile("下载😀.txt");
        const string content = "[ZoneTransfer]\r\nHostUrl=https://example.com/?token=TEST#part";
        file.WriteZone(content);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run(["debug-zone", file.Path], output, error));
        Assert.Equal(content, output.ToString());
        Assert.Contains("Raw metadata", error.ToString());
    }

    [Fact]
    public void EmptyStreamIsDistinctFromMissingStream()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run(["debug-zone", file.Path], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("stream is empty", error.ToString());
    }

    [Theory]
    [InlineData(false, 1, "No Zone.Identifier stream found.")]
    [InlineData(true, 3, "File not found.")]
    public void DistinguishesMissingFileAndStream(bool missingFile, int code, string message)
    {
        using var file = new TemporaryZoneFile();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var path = missingFile ? file.Path + ".missing" : file.Path;

        Assert.Equal(code, CommandLine.Run(["debug-zone", path], output, error));
        Assert.Equal(message + Environment.NewLine, missingFile ? error.ToString() : output.ToString());
        Assert.Equal("", missingFile ? output.ToString() : error.ToString());
    }

    [Fact]
    public void EscapesTerminalControlsButPreservesLineBreaksAndTabs()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("raw\u001b[31m\0\a\u009b\r\n\t文字");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run(["debug-zone", file.Path], output, error));
        Assert.Equal("raw\\u001b[31m\\u0000\\u0007\\u009b\r\n\t文字", output.ToString());
    }

    [Fact]
    public void ReadFailureUsesStderrAndExitCodeFour()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        using var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(4, CommandLine.Run(["debug-zone", file.Path], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Windows error 32", error.ToString());
    }

    [Theory]
    [InlineData("debug-zone")]
    [InlineData("inspect")]
    [InlineData("--json")]
    public void InvalidArgumentsShowUsage(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CommandLine.Run([argument], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void VersionIsStillAvailable()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CommandLine.Run(["--version"], output, error));
        Assert.Equal("WhereFrom 0.1.0" + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
    }
}

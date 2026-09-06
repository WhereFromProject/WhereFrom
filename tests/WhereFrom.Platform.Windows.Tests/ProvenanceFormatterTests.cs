using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class ProvenanceFormatterTests
{
    [Fact]
    public void FormatsCompleteProvenance()
    {
        var result = new ProvenanceResult(
            @"C:\Downloads\PowerToysSetup.exe",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            SourceUrl = "https://api.example.com/download?id=1",
            ReferrerUrl = "https://example.com/download",
            Zone = new(3, "Internet")
        };

        var formatted = Format(result);

        Assert.Equal(0, formatted.ExitCode);
        Assert.Equal(
            "PowerToysSetup.exe\r\n\r\n"
            + "Source\r\n  https://api.example.com/download?id=1\r\n\r\n"
            + "Referrer\r\n  https://example.com/download\r\n\r\n"
            + "Windows Zone\r\n  Internet (3)\r\n\r\n",
            NormalizeNewLines(formatted.Output));
        Assert.Equal("", formatted.Error);
    }

    [Fact]
    public void FormatsPartialZoneWithoutInventingAUrl()
    {
        var result = new ProvenanceResult(
            @"C:\Downloads\partial.zip",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            Zone = new(3, "Internet")
        };

        var formatted = Format(result);

        Assert.Equal(0, formatted.ExitCode);
        Assert.Equal(
            "partial.zip\r\n\r\n"
            + "Windows Zone\r\n  Internet (3)\r\n\r\n"
            + "No source URL recorded.\r\n",
            NormalizeNewLines(formatted.Output));
        Assert.Equal("", formatted.Error);
    }

    [Fact]
    public void KeepsReferrerWhenSourceIsMissing()
    {
        var result = new ProvenanceResult(
            "file.pdf",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            ReferrerUrl = "https://example.com/page"
        };

        var formatted = Format(result);

        Assert.Equal(0, formatted.ExitCode);
        Assert.Contains("Referrer" + Environment.NewLine, formatted.Output);
        Assert.Contains("https://example.com/page", formatted.Output);
        Assert.Contains("No source URL recorded.", formatted.Output);
        Assert.DoesNotContain("Source" + Environment.NewLine, formatted.Output);
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.NoMetadata)]
    [InlineData(ProvenanceReadStatus.Read)]
    public void NoUsableProvenanceIsACompletedUnknownResult(ProvenanceReadStatus status)
    {
        var result = new ProvenanceResult("local.txt", WindowsZoneProvider.ProviderName, status);

        var formatted = Format(result);

        Assert.Equal(1, formatted.ExitCode);
        Assert.Equal(
            "local.txt\r\n\r\nNo provenance information found.\r\n",
            NormalizeNewLines(formatted.Output));
        Assert.Equal("", formatted.Error);
    }

    [Fact]
    public void ReportsFieldProblemsWithoutDiscardingValidEvidence()
    {
        var result = new ProvenanceResult(
            "partial.exe",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            Zone = new(3, "Internet"),
            Issues = [new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue)]
        };

        var formatted = Format(result);

        Assert.Equal(0, formatted.ExitCode);
        Assert.Contains("Internet (3)", formatted.Output);
        Assert.Contains("No source URL recorded.", formatted.Output);
        Assert.Equal(
            "Some provenance metadata was incomplete, invalid, or duplicated."
            + Environment.NewLine,
            formatted.Error);
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.FileNotFound, 3, "File not found.")]
    [InlineData(ProvenanceReadStatus.NotAFile, 2, "Path is a directory; provide a file.")]
    [InlineData(ProvenanceReadStatus.InvalidPath, 2, "Invalid file path.")]
    [InlineData(ProvenanceReadStatus.AccessDenied, 3, "Access denied while reading provenance information.")]
    [InlineData(ProvenanceReadStatus.ReadFailed, 4, "Read failed.")]
    public void DistinguishesInputAndReadErrors(
        ProvenanceReadStatus status,
        int expectedCode,
        string expectedError)
    {
        var result = new ProvenanceResult("file", WindowsZoneProvider.ProviderName, status)
        {
            Error = status == ProvenanceReadStatus.ReadFailed ? expectedError : null
        };

        var formatted = Format(result);

        Assert.Equal(expectedCode, formatted.ExitCode);
        Assert.Equal("", formatted.Output);
        Assert.Equal(expectedError + Environment.NewLine, formatted.Error);
    }

    [Fact]
    public void FormatsUnknownZoneByNumber()
    {
        var result = new ProvenanceResult(
            "file.zip",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            Zone = new(1001, null)
        };

        Assert.Contains("Windows Zone" + Environment.NewLine + "  1001", Format(result).Output);
    }

    [Fact]
    public void EscapesTerminalControlsInDisplayedValues()
    {
        var result = new ProvenanceResult(
            "file\u001b[31m.txt",
            WindowsZoneProvider.ProviderName,
            ProvenanceReadStatus.Read)
        {
            SourceUrl = "https://example.com/\u001b[31m"
        };

        var formatted = Format(result);

        Assert.DoesNotContain("\u001b", formatted.Output, StringComparison.Ordinal);
        Assert.Contains(@"file\u001b[31m.txt", formatted.Output);
        Assert.Contains(@"https://example.com/\u001b[31m", formatted.Output);
    }

    private static (int ExitCode, string Output, string Error) Format(ProvenanceResult result)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return (ProvenanceFormatter.Write(result, output, error), output.ToString(), error.ToString());
    }

    private static string NormalizeNewLines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    [Fact]
    public void ErrorDetailsCannotInjectTerminalControlSequences()
    {
        var result = new ProvenanceResult("file", "test", ProvenanceReadStatus.ReadFailed)
        {
            Error = "broken\u001b[31m\nsecond line"
        };

        var formatted = Format(result);

        Assert.Equal(4, formatted.ExitCode);
        Assert.Equal("broken\\u001b[31m\\u000asecond line" + Environment.NewLine, formatted.Error);
    }
}

using System.Security.AccessControl;
using System.Security.Principal;
using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class ScanCommandTests
{
    [Theory]
    [InlineData("https://cdn.example/file", "https://github.com/repo/releases", 3, "github.com")]
    [InlineData("https://cdn.example/file", null, 3, "cdn.example")]
    [InlineData(null, null, 3, "Internet (3)")]
    [InlineData(null, null, 42, "Zone 42")]
    [InlineData("urn:example:download", null, null, "URL recorded (no host)")]
    [InlineData(null, null, null, "Unknown")]
    public void SummaryUsesExistingEvidence(string? source, string? referrer, int? zone, string expected)
    {
        var result = new ProvenanceResult("file", "test", ProvenanceReadStatus.Read)
        {
            SourceUrl = source,
            ReferrerUrl = referrer,
            Zone = zone.HasValue ? new(zone.Value, zone == 3 ? "Internet" : null) : null
        };
        Assert.Equal(expected, ScanCommand.Source(result));
    }

    [Fact]
    public void RealMixedDirectoryIsReadOnlyAndNonRecursive()
    {
        using var file = new TemporaryZoneFile("下载😀.zip");
        file.WriteZone("[ZoneTransfer]\nZoneId=3\nHostUrl=https://cdn.example/file\nReferrerUrl=https://github.com/repo/releases\0");
        var directory = Path.GetDirectoryName(file.Path)!;
        var local = Path.Combine(directory, "local.txt");
        var nested = Path.Combine(directory, "nested");
        var inner = Path.Combine(nested, "must-not-appear.txt");
        var body = File.ReadAllBytes(file.Path);
        var ads = File.ReadAllBytes(file.StreamPath);
        File.WriteAllText(local, "local");
        Directory.CreateDirectory(nested);
        File.WriteAllText(inner, "nested");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CommandLine.Run(["scan", directory], output, error));
            Assert.Contains("下载😀.zip\tgithub.com", output.ToString());
            Assert.Contains("local.txt\tUnknown", output.ToString());
            Assert.Contains("Scanned: 2", output.ToString());
            Assert.Contains("Known provenance: 1", output.ToString());
            Assert.Contains("Unknown: 1", output.ToString());
            Assert.Contains("Errors: 0", output.ToString());
            Assert.DoesNotContain("must-not-appear", output.ToString());
            Assert.Equal("", error.ToString());
            Assert.Equal(body, File.ReadAllBytes(file.Path));
            Assert.Equal(ads, File.ReadAllBytes(file.StreamPath));
            Assert.Throws<FileNotFoundException>(() => File.ReadAllBytes(local + ":Zone.Identifier"));
        }
        finally
        {
            File.Delete(inner);
            Directory.Delete(nested);
            File.Delete(local);
        }
    }

    [Fact]
    public void EmptyDirectoryCompletesWithZeroCounts()
    {
        using var file = new TemporaryZoneFile();
        File.Delete(file.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, CommandLine.Run(["scan", Path.GetDirectoryName(file.Path)!], output, error));
        Assert.Contains("Scanned: 0", output.ToString());
        Assert.Contains("Known provenance: 0", output.ToString());
        Assert.Contains("Unknown: 0", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData("\0", 2)]
    public void InvalidDirectoryHasNoReport(string path, int code)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(code, CommandLine.Run(["scan", path], output, error));
        Assert.Equal("", output.ToString());
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void MissingDirectoryAndFileInputAreDistinct()
    {
        using var file = new TemporaryZoneFile();
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(3, CommandLine.Run(["scan", file.Path + ".missing"], output, error));
        Assert.Contains("Directory not found.", error.ToString());
        Assert.Equal(2, CommandLine.Run(["scan", file.Path], output, error));
        Assert.Contains("Path is a file", error.ToString());
        Assert.Equal("", output.ToString());
    }

    [Theory]
    [InlineData("--recursive")]
    [InlineData("--json")]
    [InlineData("--unknown")]
    public void UnsupportedScanOptionsAreRejected(string option)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CommandLine.Run(["scan", ".", option], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("Usage: wherefrom scan", error.ToString());
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.AccessDenied)]
    [InlineData(ProvenanceReadStatus.FileNotFound)]
    [InlineData(ProvenanceReadStatus.ReadFailed)]
    public void FailedFileDoesNotCountAsUnknownOrStopOtherFiles(ProvenanceReadStatus status)
    {
        using var file = new TemporaryZoneFile("bad.txt");
        var directory = Path.GetDirectoryName(file.Path)!;
        var good = Path.Combine(directory, "good.txt");
        File.WriteAllText(good, "good");
        try
        {
            var provider = new StubProvider(path => new(path, "test",
                Path.GetFileName(path) == "bad.txt" ? status : ProvenanceReadStatus.NoMetadata));
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(4, CommandLine.Run(["scan", directory], output, error, provider));
            Assert.Contains("bad.txt\tError", output.ToString());
            Assert.Contains("good.txt\tUnknown", output.ToString());
            Assert.Contains("Scanned: 2", output.ToString());
            Assert.Contains("Unknown: 1", output.ToString());
            Assert.Contains("Errors: 1", output.ToString());
            Assert.Contains("bad.txt:", error.ToString());
        }
        finally
        {
            File.Delete(good);
        }
    }

    [Fact]
    public void UnexpectedProviderErrorHasNoPrivateDetails()
    {
        using var file = new TemporaryZoneFile();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var provider = new StubProvider(_ => throw new InvalidOperationException("secret"));
        Assert.Equal(5, CommandLine.Run(["scan", Path.GetDirectoryName(file.Path)!], output, error, provider));
        Assert.Contains("Errors: 1", output.ToString());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public void HiddenLongUnicodeFileIsIncluded()
    {
        using var file = new TemporaryZoneFile(new string('a', 180) + "中文.txt");
        File.SetAttributes(file.Path, FileAttributes.Hidden | FileAttributes.System);
        try
        {
            Assert.True(file.Path.Length > 260);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CommandLine.Run(["scan", Path.GetDirectoryName(file.Path)!], output, error));
            Assert.Contains(Path.GetFileName(file.Path) + "\tUnknown", output.ToString());
            Assert.Contains("Scanned: 1", output.ToString());
            Assert.Equal("", error.ToString());
        }
        finally
        {
            File.SetAttributes(file.Path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void LockedRealAdsIsAnError()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\nZoneId=3");
        using var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(4, CommandLine.Run(["scan", Path.GetDirectoryName(file.Path)!], output, error));
        Assert.Contains("Errors: 1", output.ToString());
        Assert.Contains("Unknown: 0", output.ToString());
    }

    [Fact]
    public void DeniedRootIsReportedAndDeniedChildDirectoryIsNotEntered()
    {
        using var file = new TemporaryZoneFile();
        var root = Path.GetDirectoryName(file.Path)!;
        var child = Directory.CreateDirectory(Path.Combine(root, "denied"));
        var security = child.GetAccessControl();
        var original = security.GetSecurityDescriptorBinaryForm();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            child.SetAccessControl(security);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(3, CommandLine.Run(["scan", child.FullName], output, error));
            Assert.Equal("", output.ToString());
            Assert.Contains("Access denied", error.ToString());
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            Assert.Equal(0, CommandLine.Run(["scan", root], output, error));
            Assert.Contains("Scanned: 1", output.ToString());
            Assert.Equal("", error.ToString());
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            child.SetAccessControl(restored);
            child.Delete();
        }
    }

    [Fact]
    public void RealDeniedFileIsReportedSeparately()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\nZoneId=3");
        var info = new FileInfo(file.Path);
        var security = info.GetAccessControl();
        var original = security.GetSecurityDescriptorBinaryForm();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            info.SetAccessControl(security);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(4, CommandLine.Run(["scan", Path.GetDirectoryName(file.Path)!], output, error));
            Assert.Contains("Errors: 1", output.ToString());
            Assert.Contains("Access denied", error.ToString());
        }
        finally
        {
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
    }

    private sealed class StubProvider(Func<string, ProvenanceResult> inspect) : IProvenanceProvider
    {
        public ProvenanceResult Inspect(string path) => inspect(path);
    }
}

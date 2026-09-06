using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class ZoneIdentifierReaderTests
{
    [Theory]
    [InlineData("missing.txt")]
    [InlineData("missing-directory/file.txt")]
    public void MissingFileIsDistinctFromMissingStream(string relativePath)
    {
        using var file = new TemporaryZoneFile();
        var path = Path.Combine(Path.GetDirectoryName(file.Path)!, relativePath);

        Assert.Equal(ZoneReadStatus.FileNotFound, ZoneIdentifierReader.Read(path).Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MissingStreamIsNotCreatedByReading()
    {
        using var file = new TemporaryZoneFile();

        Assert.Equal(ZoneReadStatus.StreamNotFound, ZoneIdentifierReader.Read(file.Path).Status);
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(file.StreamPath));
    }

    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/a?token=TEST#part\r\nReferrerUrl=https://example.com/\r\n")]
    [InlineData("")]
    [InlineData("[ZoneTransfer\nZoneId=invalid\nHostUrl=not a URL\nUnknownField=保留")]
    [InlineData("\0\u001b[31m\r\n")]
    public void ReturnsTextWithoutParsingOrNormalizing(string content)
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone(content);

        var result = ZoneIdentifierReader.Read(file.Path);

        Assert.Equal(ZoneReadStatus.Found, result.Status);
        Assert.Equal(content, result.Content);
    }

    [Fact]
    public void SupportsUnicodeFilePaths()
    {
        using var file = new TemporaryZoneFile("中文 下载😀.txt");
        file.WriteZone("[ZoneTransfer]\nHostUrl=https://example.com/中文");

        var result = ZoneIdentifierReader.Read(file.Path);

        Assert.Equal(ZoneReadStatus.Found, result.Status);
        Assert.Equal(File.ReadAllText(file.StreamPath), result.Content);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void DetectsByteOrderMarks(string encodingName)
    {
        using var file = new TemporaryZoneFile();
        const string content = "[ZoneTransfer]\r\nHostUrl=https://example.com/中文😀";
        file.WriteZone(content, Encoding.GetEncoding(encodingName));

        Assert.Equal(content, ZoneIdentifierReader.Read(file.Path).Content);
    }

    [Fact]
    public void InvalidUtf8IsAReadFailure()
    {
        using var file = new TemporaryZoneFile();
        File.WriteAllBytes(file.StreamPath, [0xff, 0xff]);

        Assert.Equal(ZoneReadStatus.ReadFailed, ZoneIdentifierReader.Read(file.Path).Status);
    }

    [Theory]
    [InlineData(65536, ZoneReadStatus.Found)]
    [InlineData(65537, ZoneReadStatus.ReadFailed)]
    public void EnforcesBoundedRead(int length, ZoneReadStatus expected)
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone(new string('x', length));

        Assert.Equal(expected, ZoneIdentifierReader.Read(file.Path).Status);
    }

    [Fact]
    public void ReadingPreservesBodyStreamsAndWriteTime()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/");
        var otherStream = file.Path + ":OtherEvidence";
        File.WriteAllText(otherStream, "Keep this evidence.");
        var bodyBefore = File.ReadAllBytes(file.Path);
        var zoneBefore = File.ReadAllBytes(file.StreamPath);
        var otherBefore = File.ReadAllBytes(otherStream);
        var writeTimeBefore = File.GetLastWriteTimeUtc(file.Path);

        Assert.Equal(ZoneReadStatus.Found, ZoneIdentifierReader.Read(file.Path).Status);

        Assert.Equal(bodyBefore, File.ReadAllBytes(file.Path));
        Assert.Equal(zoneBefore, File.ReadAllBytes(file.StreamPath));
        Assert.Equal(otherBefore, File.ReadAllBytes(otherStream));
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(file.Path));
    }

    [Fact]
    public void ReadOnlyFileCanBeInspected()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        File.SetAttributes(file.Path, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(ZoneReadStatus.Found, ZoneIdentifierReader.Read(file.Path).Status);
            Assert.True((File.GetAttributes(file.Path) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            File.SetAttributes(file.Path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void LockedStreamIsAReadFailure()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        using (var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = ZoneIdentifierReader.Read(file.Path);
            Assert.Equal(ZoneReadStatus.ReadFailed, result.Status);
            Assert.Contains("32", result.Error);
        }

        Assert.Equal(ZoneReadStatus.Found, ZoneIdentifierReader.Read(file.Path).Status);
    }

    [Fact]
    public void LockOnDefaultStreamDoesNotImplyAnAdsLock()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        using var locked = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(ZoneReadStatus.Found, ZoneIdentifierReader.Read(file.Path).Status);
    }

    [Fact]
    public void ReadPermissionDenialIsNotReportedAsAMissingStream()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        var info = new FileInfo(file.Path);
        var security = info.GetAccessControl();
        var original = security.GetSecurityDescriptorBinaryForm();
        security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            info.SetAccessControl(security);
            Assert.Equal(ZoneReadStatus.AccessDenied, ZoneIdentifierReader.Read(file.Path).Status);
        }
        finally
        {
            // Mark the restored ACL as modified so SetAccessControl actually persists it.
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }

        Assert.Equal(ZoneReadStatus.Found, ZoneIdentifierReader.Read(file.Path).Status);
    }

    [Fact]
    public void DirectoryIsNotAFile()
    {
        Assert.Equal(ZoneReadStatus.NotAFile, ZoneIdentifierReader.Read(AppContext.BaseDirectory).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\0")]
    public void InvalidPathsAreReported(string path)
    {
        Assert.Equal(ZoneReadStatus.InvalidPath, ZoneIdentifierReader.Read(path).Status);
    }

    [Fact]
    public void RejectsAnAlreadySpecifiedStream()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");

        Assert.Equal(ZoneReadStatus.InvalidPath, ZoneIdentifierReader.Read(file.StreamPath).Status);
    }
}

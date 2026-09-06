using System.Security.AccessControl;
using System.Security.Principal;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class WindowsZoneProviderTests
{
    private readonly IProvenanceProvider provider = new WindowsZoneProvider();

    [Fact]
    public void RealAdsMapsIntoCoreWithoutChangingEvidence()
    {
        using var file = new TemporaryZoneFile("中文😀.txt");
        const string raw = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://github.com/owner/repo/releases\r\nHostUrl=https://codeload.github.com/owner/repo/zip/v1\0";
        file.WriteZone(raw);
        var body = File.ReadAllBytes(file.Path);
        var ads = File.ReadAllBytes(file.StreamPath);
        var writeTime = File.GetLastWriteTimeUtc(file.Path);

        var result = provider.Inspect(file.Path);

        Assert.Equal(file.Path, result.Path);
        Assert.Equal(WindowsZoneProvider.ProviderName, result.Provider);
        Assert.Equal(ProvenanceReadStatus.Read, result.Status);
        Assert.Equal(new ProvenanceZone(3, "Internet"), result.Zone);
        Assert.Equal("https://codeload.github.com/owner/repo/zip/v1", result.SourceUrl);
        Assert.Equal("https://github.com/owner/repo/releases", result.ReferrerUrl);
        Assert.True(result.HasProvenance);
        Assert.Empty(result.Issues);
        Assert.Equal(body, File.ReadAllBytes(file.Path));
        Assert.Equal(ads, File.ReadAllBytes(file.StreamPath));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(file.Path));
        Assert.Equal(raw, ZoneIdentifierReader.Read(file.Path).Content);
    }

    [Fact]
    public void MissingFileAndMissingMetadataRemainDistinct()
    {
        using var file = new TemporaryZoneFile();

        var missing = provider.Inspect(file.Path + ".missing");
        var noMetadata = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.FileNotFound, missing.Status);
        Assert.Equal(ProvenanceReadStatus.NoMetadata, noMetadata.Status);
        Assert.False(missing.HasProvenance);
        Assert.False(noMetadata.HasProvenance);
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(file.StreamPath));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("broken text", false)]
    [InlineData("[ZoneTransfer]\nZoneId=3\nHostUrl=invalid", true)]
    public void ReadableButEmptyOrDamagedMetadataIsNotAnIoFailure(string content, bool hasProvenance)
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone(content);

        var result = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.Read, result.Status);
        Assert.Equal(hasProvenance, result.HasProvenance);
    }

    [Fact]
    public void DecodingFailureRemainsAReadFailure()
    {
        using var file = new TemporaryZoneFile();
        File.WriteAllBytes(file.StreamPath, [0xff, 0xff]);

        var result = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.ReadFailed, result.Status);
        Assert.False(result.HasProvenance);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void LockedStreamRemainsAReadFailure()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\nZoneId=3");
        using var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.ReadFailed, result.Status);
        Assert.Contains("32", result.Error);
        Assert.False(result.HasProvenance);
    }

    [Fact]
    public void AccessDeniedRemainsDistinctFromNoMetadata()
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
            var result = provider.Inspect(file.Path);
            Assert.Equal(ProvenanceReadStatus.AccessDenied, result.Status);
            Assert.False(result.HasProvenance);
        }
        finally
        {
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
        Assert.True(provider.Inspect(file.Path).HasProvenance);
    }

    [Fact]
    public void InvalidPathAndDirectoryRemainDistinct()
    {
        Assert.Equal(ProvenanceReadStatus.InvalidPath, provider.Inspect("").Status);
        Assert.Equal(ProvenanceReadStatus.NotAFile, provider.Inspect(AppContext.BaseDirectory).Status);
    }
}

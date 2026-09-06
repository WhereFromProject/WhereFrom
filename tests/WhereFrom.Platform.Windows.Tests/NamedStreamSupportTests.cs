using WhereFrom.Cli;
using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class NamedStreamSupportTests
{
    [Fact]
    public void RealNtfsFileSupportsNamedStreamsWithoutChangingItsBody()
    {
        using var file = new TemporaryZoneFile("能力😀.txt");
        var before = File.ReadAllBytes(file.Path);
        var writeTime = File.GetLastWriteTimeUtc(file.Path);

        Assert.True(NamedStreamSupport.Check(file.Path));
        Assert.Equal(before, File.ReadAllBytes(file.Path));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(file.Path));
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(file.StreamPath));
    }

    [Fact]
    public void CapabilityQueryDoesNotRequireOpeningBodyForReading()
    {
        using var file = new TemporaryZoneFile();
        using var locked = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.True(NamedStreamSupport.Check(file.Path));
        Assert.Equal(ProvenanceReadStatus.NoMetadata, new WindowsZoneProvider().Inspect(file.Path).Status);
    }

    [Fact]
    public void LongFilePathWithNoMetadataStillReturnsNoMetadata()
    {
        using var file = new TemporaryZoneFile(new string('a', 180) + "中文.txt");

        Assert.True(file.Path.Length > 260);
        Assert.True(NamedStreamSupport.Check(file.Path));
        Assert.Equal(ProvenanceReadStatus.NoMetadata, new WindowsZoneProvider().Inspect(file.Path).Status);
    }

    [Fact]
    public void QueryFailureDoesNotPretendTheFilesystemLacksStreams()
    {
        using var file = new TemporaryZoneFile();

        var exception = Assert.Throws<IOException>(() => NamedStreamSupport.Check(file.Path + ".missing"));
        Assert.Contains("Unable to determine", exception.Message);
    }

    [Fact]
    public void UnsupportedCapabilityProducesDistinctCliError()
    {
        using var file = new TemporaryZoneFile();
        // Simulated capability; this test does not claim to exercise an exFAT volume.
        var provider = new WindowsZoneProvider(_ => false);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(4, CommandLine.Run([file.Path], output, error, provider));
        Assert.Equal("", output.ToString());
        Assert.Equal("Alternate data streams are unavailable on this filesystem." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void FailedCapabilityQueryIsNotMisreportedAsNoProvenance()
    {
        using var file = new TemporaryZoneFile();
        var provider = new WindowsZoneProvider(_ => throw new IOException("Unable to determine stream support."));
        var result = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.ReadFailed, result.Status);
        Assert.Equal("Unable to determine stream support.", result.Error);
    }

    [Fact]
    public void ReadErrorsAlsoCheckFilesystemCapability()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        using var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var provider = new WindowsZoneProvider(_ => false);

        Assert.Equal("Alternate data streams are unavailable on this filesystem.", provider.Inspect(file.Path).Error);
    }

    [Fact]
    public void ExistingReadErrorSurvivesSecondaryQueryFailure()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("ZoneId=3");
        using var locked = new FileStream(file.StreamPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var provider = new WindowsZoneProvider(_ => throw new IOException("secondary"));

        var result = provider.Inspect(file.Path);

        Assert.Equal(ProvenanceReadStatus.ReadFailed, result.Status);
        Assert.Contains("Windows error 32", result.Error);
    }

    [Fact]
    public void SuccessfulReadsDoNotRequireVolumeManagementSupport()
    {
        using var file = new TemporaryZoneFile();
        file.WriteZone("[ZoneTransfer]\nZoneId=3");
        var provider = new WindowsZoneProvider(_ => throw new InvalidOperationException("Should not query."));

        Assert.True(provider.Inspect(file.Path).HasProvenance);
    }
}

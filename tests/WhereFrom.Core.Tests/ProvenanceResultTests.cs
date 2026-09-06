using Xunit;

namespace WhereFrom.Core.Tests;

public class ProvenanceResultTests
{
    [Fact]
    public void EmptyReadDoesNotClaimProvenance()
    {
        var result = new ProvenanceResult("file", "test", ProvenanceReadStatus.Read);

        Assert.False(result.HasProvenance);
        Assert.Null(result.Zone);
        Assert.Null(result.SourceUrl);
        Assert.Null(result.ReferrerUrl);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("source")]
    [InlineData("referrer")]
    public void AnyAvailableFieldCanProvidePartialProvenance(string field)
    {
        var result = new ProvenanceResult("file", "test", ProvenanceReadStatus.Read)
        {
            Zone = field == "zone" ? new ProvenanceZone(17, null) : null,
            SourceUrl = field == "source" ? "https://example.com/file" : null,
            ReferrerUrl = field == "referrer" ? "https://example.com/page" : null,
            Issues = [new(ProvenanceField.Metadata, ProvenanceIssueKind.InvalidFormat)]
        };

        Assert.True(result.HasProvenance);
    }

    [Theory]
    [InlineData(ProvenanceReadStatus.NoMetadata)]
    [InlineData(ProvenanceReadStatus.FileNotFound)]
    [InlineData(ProvenanceReadStatus.AccessDenied)]
    [InlineData(ProvenanceReadStatus.ReadFailed)]
    [InlineData(ProvenanceReadStatus.InvalidPath)]
    [InlineData(ProvenanceReadStatus.NotAFile)]
    public void UnsuccessfulReadNeverClaimsAvailableProvenance(ProvenanceReadStatus status)
    {
        var result = new ProvenanceResult("file", "test", status);

        Assert.False(result.HasProvenance);
        Assert.Equal(status, result.Status);
    }
}

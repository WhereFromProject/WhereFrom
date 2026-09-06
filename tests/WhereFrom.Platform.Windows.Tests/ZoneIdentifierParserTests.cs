using WhereFrom.Core;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class ZoneIdentifierParserTests
{
    private static ProvenanceResult Parse(string text) => ZoneIdentifierParser.Parse("sample.txt", text);

    [Theory]
    [InlineData("https://codeload.github.com/owner/repo/zip/refs/tags/v1", "https://github.com/owner/repo/releases")]
    [InlineData("https://example.com/api/download?id=1&token=TEST#part", "https://example.com/")]
    public void BrowserEvidencePreservesDistinctUrlsAndHandlesTrailingNul(string source, string referrer)
    {
        var text = $"[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl={referrer}\r\nHostUrl={source}\0";
        var result = Parse(text);

        Assert.Equal(ProvenanceReadStatus.Read, result.Status);
        Assert.Equal(new ProvenanceZone(3, "Internet"), result.Zone);
        Assert.Equal(source, result.SourceUrl);
        Assert.Equal(referrer, result.ReferrerUrl);
        Assert.True(result.HasProvenance);
        Assert.Empty(result.Issues);
        Assert.EndsWith("\0", text);
    }

    [Theory]
    [InlineData("ZoneId=3", 3, null, null)]
    [InlineData("HostUrl=https://example.com/file", null, "https://example.com/file", null)]
    [InlineData("ReferrerUrl=https://example.com/page", null, null, "https://example.com/page")]
    public void MissingFieldsDoNotPreventPartialResults(string fields, int? zone, string? source, string? referrer)
    {
        var result = Parse("[ZoneTransfer]\n" + fields);

        Assert.Equal(zone, result.Zone?.Id);
        Assert.Equal(source, result.SourceUrl);
        Assert.Equal(referrer, result.ReferrerUrl);
        Assert.True(result.HasProvenance);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void FieldOrderCaseWhitespaceAndOtherSectionsAreHandled()
    {
        var result = Parse("""
            HostUrl=https://ignored.example/
            [Other]
            HostUrl=https://ignored.example/
              [ zonetransfer ]
              referrerurl = https://example.com/page
            UnknownKey=anything
            HOSTURL=https://example.com/file?a=b=c
            ; comment
            # comment
            zoneid = 3
            [OtherAgain]
            ZoneId=0
            HostUrl=https://ignored.example/
            """);

        Assert.Equal(3, result.Zone?.Id);
        Assert.Equal("https://example.com/file?a=b=c", result.SourceUrl);
        Assert.Equal("https://example.com/page", result.ReferrerUrl);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0\r\n")]
    [InlineData("[ZoneTransfer]")]
    [InlineData("[ZoneTransfer]\nUnknownKey=anything")]
    public void ReadableMetadataWithoutRecognizedValuesIsNotUsableProvenance(string text)
    {
        var result = Parse(text);

        Assert.Equal(ProvenanceReadStatus.Read, result.Status);
        Assert.False(result.HasProvenance);
        Assert.Null(result.Zone);
        Assert.Null(result.SourceUrl);
        Assert.Null(result.ReferrerUrl);
    }

    [Fact]
    public void EmptyValuesRemainDistinctFromMissingFields()
    {
        var result = Parse("[ZoneTransfer]\nHostUrl= \t\nZoneId=");

        Assert.Null(result.Zone);
        Assert.Null(result.SourceUrl);
        Assert.Null(result.ReferrerUrl);
        Assert.Contains(new(ProvenanceField.Zone, ProvenanceIssueKind.EmptyValue), result.Issues);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.EmptyValue), result.Issues);
        Assert.DoesNotContain(result.Issues, issue => issue.Field == ProvenanceField.ReferrerUrl);
        Assert.False(result.HasProvenance);
    }

    [Theory]
    [InlineData("https://example.com/😀 bad")]
    [InlineData("https://example.com/😀%zz")]
    [InlineData(@"https://example.com/😀\tail")]
    [InlineData("https://example.com/{bad}")]
    [InlineData("not a URL")]
    [InlineData("https://")]
    [InlineData("/relative/path")]
    [InlineData("C:\\Downloads\\file.zip")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/%zz")]
    [InlineData("https://exa\0mple.com/file")]
    [InlineData("https://example.com/\u001b[31m")]
    public void InvalidSourceDoesNotDiscardValidZoneOrReferrer(string invalid)
    {
        var result = Parse($"[ZoneTransfer]\nZoneId=3\nHostUrl={invalid}\nReferrerUrl=https://example.com/page");

        Assert.Null(result.SourceUrl);
        Assert.Equal(3, result.Zone?.Id);
        Assert.Equal("https://example.com/page", result.ReferrerUrl);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
        Assert.True(result.HasProvenance);
    }

    [Fact]
    public void InvalidReferrerDoesNotDiscardSource()
    {
        var result = Parse("[ZoneTransfer]\nReferrerUrl=invalid\nHostUrl=https://example.com/");

        Assert.Equal("https://example.com/", result.SourceUrl);
        Assert.Null(result.ReferrerUrl);
        Assert.Contains(new(ProvenanceField.ReferrerUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
    }

    [Theory]
    [InlineData("three")]
    [InlineData("-1")]
    [InlineData("+3")]
    [InlineData("2147483648")]
    public void InvalidZoneDoesNotDiscardUrl(string invalid)
    {
        var result = Parse($"[ZoneTransfer]\nZoneId={invalid}\nHostUrl=https://example.com/");

        Assert.Null(result.Zone);
        Assert.Equal("https://example.com/", result.SourceUrl);
        Assert.Contains(new(ProvenanceField.Zone, ProvenanceIssueKind.InvalidValue), result.Issues);
    }

    [Theory]
    [InlineData(0, "Local machine")]
    [InlineData(1, "Local intranet")]
    [InlineData(2, "Trusted sites")]
    [InlineData(3, "Internet")]
    [InlineData(4, "Restricted sites")]
    [InlineData(1001, null)]
    public void ZoneNumbersArePreservedWithoutGuessingUnknownNames(int id, string? name)
    {
        var result = Parse($"[ZoneTransfer]\nZoneId={id}");

        Assert.Equal(new ProvenanceZone(id, name), result.Zone);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("https://例子.测试/下载/文件.zip?键=值#片段")]
    [InlineData("https://example.com/😀?token=TEST%2Fabc#part")]
    [InlineData("https://example.com/%00")]
    [InlineData("file:///C:/Downloads/example.zip")]
    [InlineData("ftp://example.com/file.zip")]
    [InlineData("javascript:alert(1)")]
    public void ValidAbsoluteUrisAreEvidenceWithoutBeingOpenedOrRewritten(string value)
    {
        var result = Parse("[ZoneTransfer]\nHostUrl=" + value);

        Assert.Equal(value, result.SourceUrl);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("\0\0\r\n")]
    [InlineData("\r\n\0\t ")]
    public void OnlyTrailingNulPaddingIsRemoved(string padding)
    {
        var result = Parse("[ZoneTransfer]\nHostUrl=https://example.com/" + padding);

        Assert.Equal("https://example.com/", result.SourceUrl);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void NulBeforeAnotherFieldIsNotSilentlyRemoved()
    {
        var result = Parse("[ZoneTransfer]\nHostUrl=https://example.com/\0\nZoneId=3");

        Assert.Null(result.SourceUrl);
        Assert.Equal(3, result.Zone?.Id);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
    }

    [Fact]
    public void LiteralEscapedNulIsNotTreatedAsATerminator()
    {
        var result = Parse("[ZoneTransfer]\nHostUrl=https://example.com/\\u0000");

        Assert.Null(result.SourceUrl);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("[ZoneTransfer\nHostUrl=https://example.com/")]
    [InlineData("[Other]\nZoneId=3")]
    public void MissingOrBrokenSectionDoesNotInventFields(string text)
    {
        var result = Parse(text);

        Assert.False(result.HasProvenance);
        Assert.Contains(new(ProvenanceField.Metadata, ProvenanceIssueKind.MissingSection), result.Issues);
    }

    [Fact]
    public void BrokenLinesAndSectionsDoNotEraseEarlierFields()
    {
        var result = Parse("[ZoneTransfer]\nZoneId=3\nbroken line\nHostUrl=https://example.com/\n[Broken\nReferrerUrl=https://ignored.example/");

        Assert.Equal(3, result.Zone?.Id);
        Assert.Equal("https://example.com/", result.SourceUrl);
        Assert.Null(result.ReferrerUrl);
        Assert.Contains(new(ProvenanceField.Metadata, ProvenanceIssueKind.InvalidFormat), result.Issues);
    }

    [Fact]
    public void FirstValidDuplicateWinsAndProblemsRemainVisible()
    {
        var result = Parse("""
            [ZoneTransfer]
            HostUrl=invalid
            HostUrl=https://first.example/
            HostUrl=https://second.example/
            ZoneId=3
            ZoneId=invalid
            [ZoneTransfer]
            ReferrerUrl=https://page.example/
            """);

        Assert.Equal("https://first.example/", result.SourceUrl);
        Assert.Equal(3, result.Zone?.Id);
        Assert.Equal("https://page.example/", result.ReferrerUrl);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.DuplicateField), result.Issues);
        Assert.Contains(new(ProvenanceField.Zone, ProvenanceIssueKind.InvalidValue), result.Issues);
    }

    [Fact]
    public void UnpairedSurrogatesDoNotBecomeRepairedUrls()
    {
        var malformed = "https://example.com/" + (char)0xd800;
        var result = Parse("[ZoneTransfer]\nHostUrl=" + malformed);

        Assert.Null(result.SourceUrl);
        Assert.Contains(new(ProvenanceField.SourceUrl, ProvenanceIssueKind.InvalidValue), result.Issues);
    }
}

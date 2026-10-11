namespace WhereFrom.Core;

public sealed record BrowserDownload(
    string Browser,
    string DownloadId,
    string Path,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? InitialUrl,
    string? FinalUrl,
    string? ReferrerUrl,
    bool Incognito);

public sealed record StoredDownload(
    string Browser,
    string Path,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long FileSize,
    DateTimeOffset FileLastWriteUtc,
    string? InitialUrl,
    string? FinalUrl,
    string? ReferrerUrl,
    string? FileSha256 = null)
{
    public string MatchBasis { get; init; } = "metadata";
}

public static class ProvenanceUrlSanitizer
{
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 8192
            || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}

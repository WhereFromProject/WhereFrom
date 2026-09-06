using WhereFrom.Core;

namespace WhereFrom.Platform.Windows;

public sealed class WindowsZoneProvider : IProvenanceProvider
{
    public const string ProviderName = "Windows.ZoneIdentifier";

    public ProvenanceResult Inspect(string path)
    {
        var read = ZoneIdentifierReader.Read(path);
        if (read.Status == ZoneReadStatus.Found)
        {
            return ZoneIdentifierParser.Parse(path, read.Content!);
        }

        var status = read.Status switch
        {
            ZoneReadStatus.StreamNotFound => ProvenanceReadStatus.NoMetadata,
            ZoneReadStatus.FileNotFound => ProvenanceReadStatus.FileNotFound,
            ZoneReadStatus.AccessDenied => ProvenanceReadStatus.AccessDenied,
            ZoneReadStatus.InvalidPath => ProvenanceReadStatus.InvalidPath,
            ZoneReadStatus.NotAFile => ProvenanceReadStatus.NotAFile,
            _ => ProvenanceReadStatus.ReadFailed
        };
        return new(path, ProviderName, status) { Error = read.Error };
    }
}

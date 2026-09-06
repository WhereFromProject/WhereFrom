using WhereFrom.Core;

namespace WhereFrom.Platform.Windows;

public sealed class WindowsZoneProvider : IProvenanceProvider
{
    public const string ProviderName = "Windows.ZoneIdentifier";

    private readonly Func<string, bool> supportsNamedStreams;

    public WindowsZoneProvider() : this(NamedStreamSupport.Check)
    {
    }

    internal WindowsZoneProvider(Func<string, bool> supportsNamedStreams)
    {
        this.supportsNamedStreams = supportsNamedStreams;
    }

    public ProvenanceResult Inspect(string path)
    {
        var read = ZoneIdentifierReader.Read(path);
        if (read.Status == ZoneReadStatus.Found)
        {
            return ZoneIdentifierParser.Parse(path, read.Content!);
        }

        if (read.Status is ZoneReadStatus.StreamNotFound or ZoneReadStatus.ReadFailed)
        {
            try
            {
                if (!supportsNamedStreams(path))
                {
                    return new(path, ProviderName, ProvenanceReadStatus.ReadFailed)
                    {
                        Error = "Alternate data streams are unavailable on this filesystem."
                    };
                }
            }
            catch (IOException exception)
            {
                if (read.Status == ZoneReadStatus.StreamNotFound)
                {
                    return new(path, ProviderName, ProvenanceReadStatus.ReadFailed) { Error = exception.Message };
                }
                // Preserve the original read error when a volume query is also unavailable.
            }
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

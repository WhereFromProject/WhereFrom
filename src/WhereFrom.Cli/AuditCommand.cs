using WhereFrom.Core;
using WhereFrom.Platform.Windows;
using WhereFrom.Storage;

namespace WhereFrom.Cli;

internal static class AuditCommand
{
    public static int Run(string path, int limit, TextWriter output, TextWriter error,
        IProvenanceProvider? provider = null, DownloadStore? store = null)
    {
        if (limit is < 1 or > 1000)
        {
            error.WriteLine("Audit limit must be between 1 and 1000.");
            return 2;
        }
        DirectoryInfo directory;
        try
        {
            directory = new DirectoryInfo(path);
            var attributes = File.GetAttributes(directory.FullName);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                error.WriteLine("Provide a real directory, not a file or reparse point.");
                return 2;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine("Unable to open audit directory.");
            return 3;
        }

        var query = new UnifiedProvenanceQuery(provider ?? new WindowsZoneProvider(),
            store ?? new DownloadStore(DownloadStore.DefaultPath));
        var scanned = 0;
        var known = 0;
        var possible = 0;
        var unknown = 0;
        var failed = 0;
        var skipped = 0;
        var truncated = false;
        var exitCode = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = 0
            };
            using var entries = directory.EnumerateFileSystemInfos("*", options).GetEnumerator();
            output.WriteLine("FILE\tSTATUS\tSOURCE");
            output.WriteLine("----\t------\t------");
            while (entries.MoveNext())
            {
                var entry = entries.Current;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    continue;
                }
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                    continue;
                if (scanned == limit)
                {
                    truncated = true;
                    break;
                }
                scanned++;
                var name = TerminalText.EscapeControls(entry.Name, preserveWhitespace: false);
                try
                {
                    var report = query.Inspect(entry.FullName);
                    var status = Classify(report);
                    var source = Source(report);
                    output.WriteLine($"{name}\t{status}\t{TerminalText.EscapeControls(source, preserveWhitespace: false)}");
                    switch (status)
                    {
                        case "Known": known++; break;
                        case "Possible": possible++; break;
                        case "Unknown": unknown++; break;
                        default: failed++; exitCode = Math.Max(exitCode, 4); break;
                    }
                    if (report.BrowserError is not null)
                        error.WriteLine($"{name}: Browser records unavailable.");
                }
                catch (Exception)
                {
                    failed++;
                    exitCode = Math.Max(exitCode, 4);
                    output.WriteLine($"{name}\tError\t-");
                    error.WriteLine($"{name}: Unable to inspect file.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("Audit incomplete; directory enumeration failed.");
            exitCode = Math.Max(exitCode, 4);
        }

        output.WriteLine();
        output.WriteLine($"Scanned: {scanned}");
        output.WriteLine($"Known: {known}");
        output.WriteLine($"Possible associations: {possible}");
        output.WriteLine($"Unknown: {unknown}");
        output.WriteLine($"Errors: {failed}");
        output.WriteLine($"Skipped reparse points: {skipped}");
        output.WriteLine($"Limit reached: {(truncated ? "yes" : "no")}");
        return exitCode;
    }

    private static string Classify(ProvenanceReport report)
    {
        if (report.Windows.HasProvenance || report.BrowserDownloads.Count > 0) return "Known";
        if (report.PossibleBrowserDownloads.Count > 0) return "Possible";
        if (report.BrowserError is not null || report.Windows.Status is
            ProvenanceReadStatus.ReadFailed or ProvenanceReadStatus.AccessDenied
            or ProvenanceReadStatus.FileNotFound) return "Error";
        return "Unknown";
    }

    private static string Source(ProvenanceReport report)
    {
        var url = report.SourceUrl
            ?? report.PossibleBrowserDownloads.FirstOrDefault()?.FinalUrl
            ?? report.PossibleBrowserDownloads.FirstOrDefault()?.InitialUrl;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            return uri.Host;
        if (report.Windows.Zone is { } zone)
            return zone.Name ?? $"Zone {zone.Id}";
        return "-";
    }
}

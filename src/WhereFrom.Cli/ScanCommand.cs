using WhereFrom.Core;
using WhereFrom.Platform.Windows;

namespace WhereFrom.Cli;

internal static class ScanCommand
{
    public static int Run(string path, TextWriter output, TextWriter error, IProvenanceProvider? provider = null)
    {
        var scanned = 0;
        var known = 0;
        var unknown = 0;
        var failed = 0;
        var skipped = 0;
        var exitCode = 0;
        var started = false;
        try
        {
            var directory = new DirectoryInfo(path);
            var attributes = File.GetAttributes(directory.FullName);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                error.WriteLine("Path is a file; provide a directory.");
                return 2;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                error.WriteLine("The scan directory is a reparse point; provide its target directory directly.");
                return 2;
            }

            provider ??= new WindowsZoneProvider();
            // Include hidden/system entries and surface enumeration errors. Never recurse.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = 0
            };
            using var entries = directory.EnumerateFileSystemInfos("*", options).GetEnumerator();
            // Probe enumeration before printing a report, so an inaccessible root has no stdout.
            var hasEntry = entries.MoveNext();
            output.WriteLine("FILE\tSOURCE");
            output.WriteLine("----\t------");
            started = true;
            while (hasEntry)
            {
                var entry = entries.Current;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                }
                else if ((entry.Attributes & FileAttributes.Directory) == 0)
                {
                    scanned++;
                    var name = TerminalText.EscapeControls(entry.Name, preserveWhitespace: false);
                    try
                    {
                        var result = provider.Inspect(entry.FullName);
                        using var diagnostics = new StringWriter();
                        var code = ProvenanceFormatter.Write(result, TextWriter.Null, diagnostics);
                        if (diagnostics.GetStringBuilder().Length > 0)
                        {
                            error.WriteLine($"{name}: {diagnostics.ToString().TrimEnd()}");
                        }
                        if (code is 0 or 1)
                        {
                            if (result.HasProvenance)
                            {
                                known++;
                            }
                            else
                            {
                                unknown++;
                            }
                            output.WriteLine($"{name}\t{TerminalText.EscapeControls(Source(result), preserveWhitespace: false)}");
                        }
                        else
                        {
                            failed++;
                            exitCode = Math.Max(exitCode, 4);
                            output.WriteLine($"{name}\tError");
                        }
                    }
                    catch (Exception)
                    {
                        failed++;
                        exitCode = 5;
                        output.WriteLine($"{name}\tError");
                        error.WriteLine($"{name}: An unexpected error occurred while reading provenance information.");
                    }
                }
                hasEntry = entries.MoveNext();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var (code, message) = exception switch
            {
                FileNotFoundException or DirectoryNotFoundException => (3, "Directory not found."),
                UnauthorizedAccessException => (3, "Access denied while enumerating the directory."),
                ArgumentException or NotSupportedException => (2, "Invalid directory path."),
                _ => (4, "Unable to enumerate the directory.")
            };
            error.WriteLine(message);
            if (!started)
            {
                return code;
            }
            error.WriteLine("Scan incomplete; counts include only entries processed before enumeration failed.");
            exitCode = Math.Max(exitCode, 4);
        }
        catch (Exception)
        {
            error.WriteLine("An unexpected error occurred while scanning the directory.");
            if (!started)
            {
                return 5;
            }
            error.WriteLine("Scan incomplete; counts include only entries processed before enumeration failed.");
            exitCode = 5;
        }

        output.WriteLine();
        output.WriteLine($"Scanned: {scanned}");
        output.WriteLine($"Known provenance: {known}");
        output.WriteLine($"Unknown: {unknown}");
        output.WriteLine($"Errors: {failed}");
        output.WriteLine($"Skipped reparse points: {skipped}");
        return exitCode;
    }

    internal static string Source(ProvenanceResult result)
    {
        if (!result.HasProvenance)
        {
            return "Unknown";
        }
        foreach (var value in new[] { result.ReferrerUrl, result.SourceUrl })
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            {
                return uri.Host;
            }
        }
        if (result.Zone is { } zone)
        {
            return zone.Name is null ? $"Zone {zone.Id}" : $"{zone.Name} ({zone.Id})";
        }
        // Valid hostless URIs are evidence, but do not have a host to summarize.
        return "URL recorded (no host)";
    }
}

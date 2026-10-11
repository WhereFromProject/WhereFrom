using WhereFrom.Core;
using WhereFrom.Platform.Windows;
using WhereFrom.Storage;

namespace WhereFrom.Cli;

internal static class CommandLine
{
    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        IProvenanceProvider? provider = null,
        Action<System.Diagnostics.ProcessStartInfo>? launch = null,
        DownloadStore? downloadStore = null)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            var version = typeof(CommandLine).Assembly.GetName().Version;
            output.WriteLine($"WhereFrom {version?.ToString(3)}");
            return 0;
        }

        if (args.Length == 1 && args[0] == "--help")
        {
            WriteHelp(output);
            return 0;
        }

        if (args.Length >= 1 && args[0] == "data")
        {
            if (args.Length == 2 && args[1] == "path")
            {
                output.WriteLine(DownloadStore.DefaultPath);
                return 0;
            }
            if (args.Length == 3 && args[1] == "clear" && args[2] == "--yes")
            {
                try
                {
                    output.WriteLine($"Deleted {new DownloadStore(DownloadStore.DefaultPath).Clear()} download records.");
                    return 0;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or Microsoft.Data.Sqlite.SqliteException)
                {
                    error.WriteLine("Unable to clear local download records.");
                    return 4;
                }
            }
            error.WriteLine("Usage: wherefrom data path | data clear --yes");
            return 2;
        }

        if (args.Length == 2 && args[0] == "debug-zone")
        {
            return RunDebugZone(args[1], output, error);
        }

        if (args.Length >= 1 && args[0] == "audit")
        {
            var valid = (args.Length == 2 || args.Length == 4
                && args[2] == "--limit" && int.TryParse(args[3], out _))
                && args[1].Length > 0 && !args[1].StartsWith('-');
            if (!valid)
            {
                error.WriteLine("Usage: wherefrom audit <directory> [--limit 1..1000]");
                return 2;
            }
            var limit = args.Length == 4 ? int.Parse(args[3]) : 100;
            return AuditCommand.Run(args[1], limit, output, error, provider, downloadStore);
        }
        if (args.Length >= 2 && args[0] == "scan" && args[1] != "--json")
        {
            if (args.Length != 2 || args[1].StartsWith('-'))
            {
                error.WriteLine("Usage: wherefrom scan <directory>");
                return 2;
            }
            return ScanCommand.Run(args[1], output, error, provider);
        }

        if (args.Length >= 2 && args[0] == "open" && args[1] != "--json")
        {
            if (args.Length != 2 || args[1].StartsWith('-'))
            {
                error.WriteLine("Usage: wherefrom open <file>");
                return 2;
            }
            return OpenCommand.Run(args[1], output, error, provider, launch, downloadStore);
        }

        var json = args.Length == 2 && args[1] == "--json";

        if ((!json && args.Length != 1) || args[0].StartsWith('-') || args[0] == "debug-zone")
        {
            error.WriteLine("Usage: wherefrom <file> | --help | --version");
            return 2;
        }

        try
        {
            var windows = provider ?? new WindowsZoneProvider();
            var report = provider is not null && downloadStore is null
                ? new ProvenanceReport(windows.Inspect(args[0]), [], 0, null)
                : new UnifiedProvenanceQuery(windows, downloadStore ?? new DownloadStore(DownloadStore.DefaultPath))
                    .Inspect(args[0]);
            var exitCode = ProvenanceFormatter.Write(report, json ? TextWriter.Null : output, error);
            if (json && exitCode is 0 or 1)
            {
                output.WriteLine(ProvenanceJson.Serialize(report));
            }
            return exitCode;
        }
        catch (Exception)
        {
            error.WriteLine("An unexpected error occurred while reading provenance information.");
            return 5;
        }
    }

    private static int RunDebugZone(string path, TextWriter output, TextWriter error)
    {
        var result = ZoneIdentifierReader.Read(path);
        switch (result.Status)
        {
            case ZoneReadStatus.Found:
                if (result.Content!.Length == 0)
                {
                    error.WriteLine("Zone.Identifier stream is empty.");
                }
                else
                {
                    error.WriteLine("Raw metadata may contain private URLs or tokens.");
                    output.Write(TerminalText.EscapeControls(result.Content));
                }
                return 0;
            case ZoneReadStatus.StreamNotFound:
                output.WriteLine("No Zone.Identifier stream found.");
                return 1;
            case ZoneReadStatus.FileNotFound:
                error.WriteLine("File not found.");
                return 3;
            case ZoneReadStatus.AccessDenied:
                error.WriteLine("Access denied while probing the file or Zone.Identifier.");
                return 3;
            case ZoneReadStatus.NotAFile:
                error.WriteLine("Path is a directory; provide a file.");
                return 2;
            case ZoneReadStatus.InvalidPath:
                error.WriteLine("Invalid file path; provide a file path without a stream suffix.");
                return 2;
            default:
                error.WriteLine(result.Error ?? "Unable to read Zone.Identifier.");
                return 4;
        }
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("WhereFrom - show where a Windows file came from");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  wherefrom <file>");
        output.WriteLine("  wherefrom <file> --json");
        output.WriteLine("  wherefrom scan <directory>");
        output.WriteLine("  wherefrom audit <directory> [--limit 1..1000]");
        output.WriteLine("  wherefrom open <file>");
        output.WriteLine("  wherefrom --help");
        output.WriteLine("  wherefrom --version");
        output.WriteLine("  wherefrom data path");
        output.WriteLine("  wherefrom data clear --yes");
        output.WriteLine();
        output.WriteLine("Diagnostic:");
        output.WriteLine("  wherefrom debug-zone <file>  Show raw Zone.Identifier metadata");
    }
}

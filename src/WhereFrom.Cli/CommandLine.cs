using WhereFrom.Core;
using WhereFrom.Platform.Windows;

namespace WhereFrom.Cli;

internal static class CommandLine
{
    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        IProvenanceProvider? provider = null)
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

        if (args.Length == 2 && args[0] == "debug-zone")
        {
            return RunDebugZone(args[1], output, error);
        }

        var json = args.Length == 2 && args[1] == "--json";

        if ((!json && args.Length != 1) || args[0].StartsWith('-') || args[0] == "debug-zone")
        {
            error.WriteLine("Usage: wherefrom <file> | --help | --version");
            return 2;
        }

        try
        {
            var result = (provider ?? new WindowsZoneProvider()).Inspect(args[0]);
            // Reuse the existing diagnostics and exit codes without changing text mode.
            var exitCode = ProvenanceFormatter.Write(result, json ? TextWriter.Null : output, error);
            if (json && exitCode is 0 or 1)
            {
                output.WriteLine(ProvenanceJson.Serialize(result));
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
        output.WriteLine("  wherefrom --help");
        output.WriteLine("  wherefrom --version");
        output.WriteLine();
        output.WriteLine("Diagnostic:");
        output.WriteLine("  wherefrom debug-zone <file>  Show raw Zone.Identifier metadata");
    }
}

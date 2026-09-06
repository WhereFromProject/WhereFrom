using System.Text;
using WhereFrom.Platform.Windows;

namespace WhereFrom.Cli;

internal static class CommandLine
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "--version"))
        {
            var version = typeof(CommandLine).Assembly.GetName().Version;
            output.WriteLine($"WhereFrom {version?.ToString(3)}");
            return 0;
        }

        if (args.Length != 2 || args[0] != "debug-zone")
        {
            error.WriteLine("Usage: wherefrom debug-zone <file> | --version");
            return 2;
        }

        var result = ZoneIdentifierReader.Read(args[1]);
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
                    output.Write(EscapeControls(result.Content));
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

    private static string EscapeControls(string content)
    {
        var output = new StringBuilder(content.Length);
        foreach (var character in content)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                output.Append($"\\u{(int)character:x4}");
            }
            else
            {
                output.Append(character);
            }
        }
        return output.ToString();
    }
}

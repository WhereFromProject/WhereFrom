using System.ComponentModel;
using System.Diagnostics;
using WhereFrom.Core;
using WhereFrom.Platform.Windows;

namespace WhereFrom.Cli;

internal static class OpenCommand
{
    public static int Run(string path, TextWriter output, TextWriter error,
        IProvenanceProvider? provider = null, Action<ProcessStartInfo>? launch = null)
    {
        try
        {
            var result = (provider ?? new WindowsZoneProvider()).Inspect(path);
            var code = ProvenanceFormatter.Write(result, TextWriter.Null, error);
            if (code is not (0 or 1))
            {
                return code;
            }
            var url = result.ReferrerUrl ?? result.SourceUrl;
            if (url is null)
            {
                output.WriteLine("No source URL is available for this file.");
                return 1;
            }

            ProcessStartInfo startInfo;
            try
            {
                startInfo = BrowserLauncher.CreateStartInfo(url);
            }
            catch (ArgumentException)
            {
                error.WriteLine("Refusing to open the selected source URL. Only valid HTTP or HTTPS URLs are allowed.");
                return 2;
            }

            output.WriteLine("Opening:");
            output.WriteLine(TerminalText.EscapeControls(url, preserveWhitespace: false));
            try
            {
                if (launch is null)
                {
                    using var process = Process.Start(startInfo);
                }
                else
                {
                    launch(startInfo);
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                error.WriteLine("Unable to open the source URL in the default browser.");
                return 4;
            }
            return 0;
        }
        catch (Exception)
        {
            error.WriteLine("An unexpected error occurred while opening the source URL.");
            return 5;
        }
    }
}

using WhereFrom.Core;

namespace WhereFrom.Cli;

internal static class ProvenanceFormatter
{
    public static int Write(ProvenanceReport report, TextWriter output, TextWriter error)
    {
        var windows = report.Windows;
        if (report.BrowserDownloads.Count == 0 && report.PossibleBrowserDownloads.Count == 0)
        {
            var code = Write(windows, output, error);
            if (windows.HasProvenance && code == 0)
                output.WriteLine("Evidence: Windows Zone.Identifier");
            if (report.HistoricalBrowserRecords > 0)
                output.WriteLine("Historical browser records at this path were excluded because the file changed.");
            if (report.BrowserError is not null)
            {
                error.WriteLine("Browser records unavailable.");
                return code == 1 ? 4 : code;
            }
            return code;
        }

        output.WriteLine(TerminalText.EscapeControls(Path.GetFileName(windows.Path), preserveWhitespace: false));
        output.WriteLine();
        if (windows.SourceUrl is not null)
            WriteSection(output, "Windows source (Zone.Identifier; may contain private URL parameters)", windows.SourceUrl);
        if (windows.ReferrerUrl is not null)
            WriteSection(output, "Windows referrer", windows.ReferrerUrl);
        if (windows.Zone is not null)
            WriteSection(output, "Windows Zone", windows.Zone.Name is null
                ? windows.Zone.Id.ToString() : $"{windows.Zone.Name} ({windows.Zone.Id})");
        foreach (var download in report.BrowserDownloads)
        {
            output.WriteLine(download.MatchBasis == "sha256"
                ? $"{download.Browser} download (path, size, time and SHA-256 match; content may be a copy)"
                : $"{download.Browser} download (only path, size and time match; content unverified)");
            if ((download.FinalUrl ?? download.InitialUrl) is { } source)
                output.WriteLine($"  Source: {TerminalText.EscapeControls(source, preserveWhitespace: false)}");
            if (download.ReferrerUrl is not null)
                output.WriteLine($"  Referrer: {TerminalText.EscapeControls(download.ReferrerUrl, preserveWhitespace: false)}");
            output.WriteLine();
        }
        foreach (var download in report.PossibleBrowserDownloads)
        {
            output.WriteLine(download.MatchBasis == "sha256"
                ? $"Possible {download.Browser} download association (same SHA-256 content; may be a move or copy)"
                : $"Possible {download.Browser} download association (only path, size and time match; content unverified)");
            output.WriteLine($"  Previously recorded path: {TerminalText.EscapeControls(download.Path, preserveWhitespace: false)}");
            if ((download.FinalUrl ?? download.InitialUrl) is { } source)
                output.WriteLine($"  Possible source: {TerminalText.EscapeControls(source, preserveWhitespace: false)}");
            if (download.ReferrerUrl is not null)
                output.WriteLine($"  Possible referrer: {TerminalText.EscapeControls(download.ReferrerUrl, preserveWhitespace: false)}");
            output.WriteLine();
        }
        if (report.HasConflict)
            output.WriteLine("Windows and browser source URLs differ; both are shown without choosing one.");
        if (report.HistoricalBrowserRecords > 0)
            output.WriteLine("Historical browser records at this path were excluded because the file changed.");
        if (windows.Status is ProvenanceReadStatus.ReadFailed or ProvenanceReadStatus.AccessDenied)
            error.WriteLine("Windows provenance metadata could not be read.");
        if (report.BrowserError is not null)
            error.WriteLine("Browser records unavailable.");
        WriteIssues(windows, error);
        return 0;
    }

    public static int Write(ProvenanceResult result, TextWriter output, TextWriter error)
    {
        switch (result.Status)
        {
            case ProvenanceReadStatus.FileNotFound:
                error.WriteLine("File not found.");
                return 3;
            case ProvenanceReadStatus.NotAFile:
                error.WriteLine("Path is a directory; provide a file.");
                return 2;
            case ProvenanceReadStatus.InvalidPath:
                error.WriteLine("Invalid file path.");
                return 2;
            case ProvenanceReadStatus.AccessDenied:
                error.WriteLine("Access denied while reading provenance information.");
                return 3;
            case ProvenanceReadStatus.ReadFailed:
                error.WriteLine(TerminalText.EscapeControls(result.Error ?? "Unable to read provenance information.", preserveWhitespace: false));
                return 4;
        }

        var fileName = Path.GetFileName(result.Path);
        output.WriteLine(TerminalText.EscapeControls(fileName, preserveWhitespace: false));
        output.WriteLine();

        if (!result.HasProvenance)
        {
            output.WriteLine("No provenance information found.");
            WriteIssues(result, error);
            return 1;
        }

        if (result.SourceUrl is not null)
        {
            WriteSection(output, "Source", result.SourceUrl);
        }

        if (result.ReferrerUrl is not null)
        {
            WriteSection(output, "Referrer", result.ReferrerUrl);
        }

        if (result.Zone is not null)
        {
            var value = result.Zone.Name is null
                ? result.Zone.Id.ToString()
                : $"{result.Zone.Name} ({result.Zone.Id})";
            WriteSection(output, "Windows Zone", value);
        }

        if (result.SourceUrl is null)
        {
            output.WriteLine("No source URL recorded.");
        }

        WriteIssues(result, error);
        return 0;
    }

    private static void WriteSection(TextWriter output, string heading, string value)
    {
        output.WriteLine(heading);
        output.WriteLine($"  {TerminalText.EscapeControls(value, preserveWhitespace: false)}");
        output.WriteLine();
    }

    private static void WriteIssues(ProvenanceResult result, TextWriter error)
    {
        if (result.Issues.Count > 0)
        {
            error.WriteLine("Some provenance metadata was incomplete, invalid, or duplicated.");
        }
    }
}

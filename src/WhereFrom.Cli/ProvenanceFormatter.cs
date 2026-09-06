using WhereFrom.Core;

namespace WhereFrom.Cli;

internal static class ProvenanceFormatter
{
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

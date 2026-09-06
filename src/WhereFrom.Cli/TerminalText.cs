using System.Text;

namespace WhereFrom.Cli;

internal static class TerminalText
{
    public static string EscapeControls(string value, bool preserveWhitespace = true)
    {
        var output = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character) && (!preserveWhitespace || character is not '\r' and not '\n' and not '\t'))
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

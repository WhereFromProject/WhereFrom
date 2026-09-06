using System.Globalization;
using WhereFrom.Core;

namespace WhereFrom.Platform.Windows;

internal static class ZoneIdentifierParser
{
    public static ProvenanceResult Parse(string path, string rawText)
    {
        ProvenanceZone? zone = null;
        string? sourceUrl = null;
        string? referrerUrl = null;
        var issues = new List<ProvenanceIssue>();
        var seenFields = new HashSet<ProvenanceField>();
        var inZoneSection = false;
        var foundZoneSection = false;

        // Real browser samples ended in NUL. Strip only the trailing terminator/padding
        // from this parsing copy; embedded NULs and the reader's original text stay intact.
        var text = rawText.TrimEnd('\0', '\r', '\n', ' ', '\t');
        using var lines = new StringReader(text);
        while (lines.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim(' ', '\t');
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                {
                    issues.Add(new(ProvenanceField.Metadata, ProvenanceIssueKind.InvalidFormat));
                    inZoneSection = false;
                    continue;
                }
                inZoneSection = line[1..^1].Trim(' ', '\t')
                    .Equals("ZoneTransfer", StringComparison.OrdinalIgnoreCase);
                foundZoneSection |= inZoneSection;
                continue;
            }

            if (!inZoneSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                issues.Add(new(ProvenanceField.Metadata, ProvenanceIssueKind.InvalidFormat));
                continue;
            }

            var key = line[..separator].Trim(' ', '\t');
            var field = key.ToUpperInvariant() switch
            {
                "ZONEID" => ProvenanceField.Zone,
                "HOSTURL" => ProvenanceField.SourceUrl,
                "REFERRERURL" => ProvenanceField.ReferrerUrl,
                _ => (ProvenanceField?)null
            };
            if (field is null)
            {
                continue;
            }

            if (!seenFields.Add(field.Value))
            {
                issues.Add(new(field.Value, ProvenanceIssueKind.DuplicateField));
            }

            var value = line[(separator + 1)..].Trim(' ', '\t');
            if (value.Length == 0)
            {
                issues.Add(new(field.Value, ProvenanceIssueKind.EmptyValue));
                continue;
            }

            if (field == ProvenanceField.Zone)
            {
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    // Keep the first valid occurrence, including when an earlier one was invalid.
                    zone ??= new(id, id switch
                    {
                        0 => "Local machine",
                        1 => "Local intranet",
                        2 => "Trusted sites",
                        3 => "Internet",
                        4 => "Restricted sites",
                        _ => null
                    });
                }
                else
                {
                    issues.Add(new(field.Value, ProvenanceIssueKind.InvalidValue));
                }
                continue;
            }

            // Validate without returning the canonicalized URI: URLs remain evidence text.
            if (!IsValidUrl(value))
            {
                issues.Add(new(field.Value, ProvenanceIssueKind.InvalidValue));
                continue;
            }

            if (field == ProvenanceField.SourceUrl)
            {
                sourceUrl ??= value;
            }
            else
            {
                referrerUrl ??= value;
            }
        }

        if (!foundZoneSection && text.Length > 0)
        {
            issues.Add(new(ProvenanceField.Metadata, ProvenanceIssueKind.MissingSection));
        }

        return new(path, WindowsZoneProvider.ProviderName, ProvenanceReadStatus.Read)
        {
            Zone = zone,
            SourceUrl = sourceUrl,
            ReferrerUrl = referrerUrl,
            Issues = issues.AsReadOnly()
        };
    }

    private static bool IsValidUrl(string value)
    {
        // TryCreate tolerates some malformed characters; reject them before parsing.
        // IsWellFormedOriginalString rejects some valid non-BMP IRI characters on .NET 10.
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsControl(character) || char.IsWhiteSpace(character)
                || character == '\\' || character is '<' or '>' or '"' or '{' or '}' or '|' or '^' or '`')
            {
                return false;
            }

            if (character == '%' && (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])))
            {
                return false;
            }

            if (char.IsSurrogate(character))
            {
                if (!char.IsHighSurrogate(character) || index + 1 >= value.Length
                    || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }
                index++;
            }
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && value.StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase);
    }
}

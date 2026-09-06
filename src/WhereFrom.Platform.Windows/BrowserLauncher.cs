using System.Diagnostics;

namespace WhereFrom.Platform.Windows;

public static class BrowserLauncher
{
    public static ProcessStartInfo CreateStartInfo(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Host.Length == 0
            || !url.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only valid HTTP or HTTPS URLs can be opened.", nameof(url));
        }

        // Reject characters that URI parsing might silently normalize before shell dispatch.
        for (var index = 0; index < url.Length; index++)
        {
            var character = url[index];
            if (char.IsControl(character) || char.IsWhiteSpace(character)
                || character is '\\' or '"' or '<' or '>' or '{' or '}' or '|' or '^' or '`'
                || (character == '%' && (index + 2 >= url.Length
                    || !Uri.IsHexDigit(url[index + 1]) || !Uri.IsHexDigit(url[index + 2]))))
            {
                throw new ArgumentException("Only valid HTTP or HTTPS URLs can be opened.", nameof(url));
            }
            if (char.IsSurrogate(character))
            {
                if (!char.IsHighSurrogate(character) || index + 1 >= url.Length
                    || !char.IsLowSurrogate(url[index + 1]))
                {
                    throw new ArgumentException("Only valid HTTP or HTTPS URLs can be opened.", nameof(url));
                }
                index++;
            }
        }

        // Windows resolves the registered URL handler. No command interpreter or arguments.
        return new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" };
    }
}

using System.Text;

namespace WhereFrom.Platform.Windows;

public enum ZoneReadStatus
{
    Found,
    FileNotFound,
    StreamNotFound,
    AccessDenied,
    ReadFailed,
    InvalidPath,
    NotAFile
}

// This is a diagnostic I/O result, not the future provenance domain model.
public sealed record ZoneReadResult(ZoneReadStatus Status, string? Content = null, string? Error = null);

public static class ZoneIdentifierReader
{
    public const int MaximumBytes = 64 * 1024;

    public static ZoneReadResult Read(string path)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var fullPath = Path.GetFullPath(path);
            if (fullPath.AsSpan(Path.GetPathRoot(fullPath)!.Length).Contains(':'))
            {
                return new(ZoneReadStatus.InvalidPath);
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.Directory) != 0)
            {
                return new(ZoneReadStatus.NotAFile);
            }

            try
            {
                using var stream = new FileStream(fullPath + ":Zone.Identifier",
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                // Bound the actual read as well as memory use, even if another process grows the stream.
                var bytes = new byte[MaximumBytes + 1];
                var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                if (length > MaximumBytes)
                {
                    return new(ZoneReadStatus.ReadFailed, Error: "Zone.Identifier exceeds the 64 KiB limit.");
                }

                using var content = new MemoryStream(bytes, 0, length);
                using var reader = new StreamReader(content, new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: true);
                return new(ZoneReadStatus.Found, reader.ReadToEnd());
            }
            catch (FileNotFoundException)
            {
                // A missing stream and a missing file both use ERROR_FILE_NOT_FOUND.
                // Recheck the base file so a deletion during the probe is not hidden.
                File.GetAttributes(fullPath);
                return new(ZoneReadStatus.StreamNotFound);
            }
        }
        catch (FileNotFoundException)
        {
            return new(ZoneReadStatus.FileNotFound);
        }
        catch (DirectoryNotFoundException)
        {
            return new(ZoneReadStatus.FileNotFound);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ZoneReadStatus.AccessDenied);
        }
        catch (DecoderFallbackException)
        {
            return new(ZoneReadStatus.ReadFailed, Error: "Zone.Identifier is not valid UTF-8 text.");
        }
        catch (ArgumentException)
        {
            return new(ZoneReadStatus.InvalidPath);
        }
        catch (NotSupportedException)
        {
            return new(ZoneReadStatus.InvalidPath);
        }
        catch (IOException exception)
        {
            return new(ZoneReadStatus.ReadFailed,
                Error: $"Unable to read Zone.Identifier (Windows error {exception.HResult & 0xffff}).");
        }
    }
}

using System.Text;

namespace WhereFrom.Platform.Windows.Tests;

// All writes and cleanup are confined to a new fixture below this project's bin directory.
internal sealed class TemporaryZoneFile : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(
        AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));

    public string Path { get; }
    public string StreamPath => Path + ":Zone.Identifier";

    public TemporaryZoneFile(string name = "sample.txt")
    {
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, name);
        File.WriteAllText(Path, "Test fixture body.");
    }

    public void WriteZone(string content, Encoding? encoding = null)
    {
        File.WriteAllText(StreamPath, content, encoding ?? new UTF8Encoding(false));
    }

    public void Dispose()
    {
        File.Delete(Path);
        Directory.Delete(directory);
    }
}

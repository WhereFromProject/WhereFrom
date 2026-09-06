using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WhereFrom.Platform.Windows;

internal static class NamedStreamSupport
{
    private const uint FileNamedStreams = 0x00040000;

    public static bool Check(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length >= 260 && !fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            fullPath = fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + fullPath[2..]
                : @"\\?\" + fullPath;
        }

        // Request metadata access only. Opening the default data stream for reading
        // would incorrectly fail when that stream is locked but ADS can still be queried.
        using var file = CreateFile(fullPath, 0,
            FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (file.IsInvalid)
        {
            throw QueryFailed();
        }

        if (!GetVolumeInformationByHandle(file, IntPtr.Zero, 0, IntPtr.Zero,
            IntPtr.Zero, out var flags, IntPtr.Zero, 0))
        {
            throw QueryFailed();
        }

        return (flags & FileNamedStreams) != 0;
    }

    private static IOException QueryFailed() => new(
        $"Unable to determine alternate data stream availability (Windows error {Marshal.GetLastWin32Error()}).");

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, FileShare shareMode, IntPtr securityAttributes,
        FileMode creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandle(
        SafeFileHandle file, IntPtr volumeNameBuffer, uint volumeNameSize, IntPtr volumeSerialNumber,
        IntPtr maximumComponentLength, out uint fileSystemFlags, IntPtr fileSystemNameBuffer,
        uint fileSystemNameSize);
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CopyPreflight.Windows;

/// <summary>
/// The real Windows file-system queries, implemented with direct <c>kernel32</c> interop.
/// </summary>
/// <remarks>
/// <para>
/// Only the four calls the target probe actually makes are declared, each with the Unicode entry
/// point, <c>SetLastError</c> and an explicit entry point name. No third-party interop package is
/// used, and no broader Kernel32 surface is wrapped speculatively.
/// </para>
/// <para>
/// The native failure sentinel and error code are converted into the seam's contract here, so the
/// probe never has to reason about marshalling.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class Kernel32FileSystemApi : IWindowsFileSystemApi
{
    private const string Kernel32Library = "kernel32.dll";

    /// <summary>
    /// <c>GetFileAttributesW</c> reports this value when it fails. No real object carries it.
    /// </summary>
    private static readonly FileAttributes InvalidFileAttributes = (FileAttributes)(-1);

    /// <summary>
    /// Room for the volume mount point. Mount points are ordinary paths, so the reported value stays
    /// well inside this; a volume path too long to fit surfaces as a failed query rather than a
    /// truncated one.
    /// </summary>
    private const int VolumePathBufferLength = 4096;

    /// <summary>
    /// Room for the file system name, which is a short token such as <c>NTFS</c>.
    /// </summary>
    private const int FileSystemNameBufferLength = 261;

    /// <inheritdoc />
    public bool TryGetFileAttributes(string path, out FileAttributes attributes, out int nativeError)
    {
        var reported = GetFileAttributesW(path);

        if (reported == InvalidFileAttributes)
        {
            attributes = default;
            nativeError = Marshal.GetLastWin32Error();
            return false;
        }

        attributes = reported;
        nativeError = 0;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetFreeBytesAvailableToCaller(
        string directoryPath,
        out ulong freeBytesAvailableToCaller,
        out int nativeError)
    {
        // Only the first output is read: lpFreeBytesAvailableToCaller is the quota-aware amount the
        // calling user may still use, which is the quantity a migration actually depends on.
        // lpTotalNumberOfFreeBytes deliberately is not used as a substitute for it.
        var succeeded = GetDiskFreeSpaceExW(
            directoryPath,
            out freeBytesAvailableToCaller,
            out _,
            out _);

        if (!succeeded)
        {
            freeBytesAvailableToCaller = 0;
            nativeError = Marshal.GetLastWin32Error();
            return false;
        }

        nativeError = 0;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetVolumePathName(string path, out string volumePathName, out int nativeError)
    {
        var buffer = new char[VolumePathBufferLength];

        if (!GetVolumePathNameW(path, buffer, (uint)buffer.Length))
        {
            volumePathName = string.Empty;
            nativeError = Marshal.GetLastWin32Error();
            return false;
        }

        nativeError = 0;

        // The reported path is NUL-terminated. The length is bounded by the buffer, so a missing
        // terminator cannot read past it.
        var terminatorIndex = Array.IndexOf(buffer, '\0');
        var reportedLength = terminatorIndex < 0 ? buffer.Length : terminatorIndex;

        volumePathName = new string(buffer, 0, reportedLength);
        return volumePathName.Length > 0;
    }

    /// <inheritdoc />
    public bool TryGetVolumeInformation(
        string volumeRootPath,
        out string fileSystemName,
        out uint maximumComponentLength,
        out int nativeError)
    {
        var nameBuffer = new StringBuilder(FileSystemNameBufferLength);

        // The volume label is not needed and is left unrequested; only the file system name and the
        // maximum component length are read.
        var succeeded = GetVolumeInformationW(
            volumeRootPath,
            lpVolumeNameBuffer: null,
            nVolumeNameSize: 0,
            out _,
            out maximumComponentLength,
            out _,
            nameBuffer,
            (uint)nameBuffer.Capacity);

        if (!succeeded)
        {
            fileSystemName = string.Empty;
            maximumComponentLength = 0;
            nativeError = Marshal.GetLastWin32Error();
            return false;
        }

        nativeError = 0;
        fileSystemName = nameBuffer.ToString();
        return true;
    }

    [DllImport(
        Kernel32Library,
        EntryPoint = "GetFileAttributesW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern FileAttributes GetFileAttributesW(string lpFileName);

    [DllImport(
        Kernel32Library,
        EntryPoint = "GetDiskFreeSpaceExW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    [DllImport(
        Kernel32Library,
        EntryPoint = "GetVolumePathNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName,
        [Out] char[] lpszVolumePathName,
        uint cchBufferLength);

    [DllImport(
        Kernel32Library,
        EntryPoint = "GetVolumeInformationW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        [Out] StringBuilder lpFileSystemNameBuffer,
        uint nFileSystemNameSize);
}

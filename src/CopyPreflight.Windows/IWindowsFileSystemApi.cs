using System.Runtime.Versioning;

namespace CopyPreflight.Windows;

/// <summary>
/// The Windows file-system queries the target probe performs, behind a seam.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately tiny internal seam, not a virtual file system and not a general operating
/// system abstraction. It exists because some outcomes this probe must handle correctly cannot be
/// produced honestly on a developer machine: a volume whose information query fails while its
/// free-space query succeeds (an ordinary SMB or NAS target), a native free-space value larger than
/// the capability model can represent, and a volume reporting a component length of zero. Testing
/// those only against the real API would require a real network share, a quota, or a hand-made
/// volume, none of which may be a precondition for running the test suite.
/// </para>
/// <para>
/// Every member mirrors one Win32 call and keeps its native failure visible, so a test can drive
/// success, partial failure and a specific native error. The interface is not product API and is not
/// intended to grow: it covers exactly the queries M3 makes.
/// </para>
/// <para>
/// A member returns <see langword="false"/> when Windows did not answer the question. It does not
/// throw for an unanswerable question, and it never substitutes a plausible value for a missing one.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal interface IWindowsFileSystemApi
{
    /// <summary>Reads the attributes of the object at a path.</summary>
    /// <param name="path">The path to inspect, as it should be handed to Windows.</param>
    /// <param name="attributes">The reported attributes when the call succeeds; otherwise unspecified.</param>
    /// <param name="nativeError">
    /// The error Windows reported when the call fails, or <c>0</c> (<c>ERROR_SUCCESS</c>) when it
    /// succeeds. <c>0</c> therefore means "no error to report", not a missing value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the object exists and its attributes were read. A path that does
    /// not exist, cannot be reached or is malformed returns <see langword="false"/>.
    /// </returns>
    bool TryGetFileAttributes(string path, out FileAttributes attributes, out int nativeError);

    /// <summary>Reads the space available on a volume to the calling user.</summary>
    /// <param name="directoryPath">A directory on the volume to measure.</param>
    /// <param name="freeBytesAvailableToCaller">
    /// The bytes the calling user may still use on that volume when the call succeeds; otherwise
    /// unspecified. This is the quota-aware figure, not the volume's total free space.
    /// </param>
    /// <param name="nativeError">
    /// The error Windows reported when the call fails, or <c>0</c> when it succeeds.
    /// </param>
    /// <returns><see langword="true"/> when Windows reported an amount available to the caller.</returns>
    bool TryGetFreeBytesAvailableToCaller(
        string directoryPath,
        out ulong freeBytesAvailableToCaller,
        out int nativeError);

    /// <summary>Resolves the volume mount point Windows considers a path to belong to.</summary>
    /// <param name="path">The path to resolve.</param>
    /// <param name="volumePathName">
    /// The volume mount point when one was reported, for example <c>C:\</c> or <c>C:\Mount\</c>;
    /// otherwise empty. It is what Windows returned, never a root derived from the input.
    /// </param>
    /// <param name="nativeError">
    /// The error Windows reported when the call fails, or <c>0</c> when it succeeds. A call that
    /// reports no usable volume path also yields <c>0</c>: the call itself did not fail, it simply
    /// answered nothing.
    /// </param>
    /// <returns><see langword="true"/> when a non-empty volume mount point was reported.</returns>
    bool TryGetVolumePathName(string path, out string volumePathName, out int nativeError);

    /// <summary>Describes the file system on a volume.</summary>
    /// <param name="volumeRootPath">
    /// The root of the volume to describe. Windows requires the root of a volume here and rejects a
    /// path inside one.
    /// </param>
    /// <param name="fileSystemName">
    /// The file system name Windows reported, for example <c>NTFS</c>; otherwise empty.
    /// </param>
    /// <param name="maximumComponentLength">
    /// The maximum length of a single path component Windows reported for that file system;
    /// otherwise unspecified. It is the reported value, not a value inferred from the file system's
    /// usual behaviour.
    /// </param>
    /// <param name="nativeError">
    /// The error Windows reported when the call fails, or <c>0</c> when it succeeds.
    /// </param>
    /// <returns><see langword="true"/> when Windows described the volume.</returns>
    bool TryGetVolumeInformation(
        string volumeRootPath,
        out string fileSystemName,
        out uint maximumComponentLength,
        out int nativeError);
}

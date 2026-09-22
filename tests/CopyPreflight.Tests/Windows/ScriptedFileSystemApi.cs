using System.IO;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Supplies the target probe with scripted answers to the Windows queries it makes.
/// </summary>
/// <remarks>
/// <para>
/// Used only for the outcomes a developer machine cannot be made to produce honestly: a volume whose
/// information query fails while its free-space query succeeds — which is what an ordinary SMB or NAS
/// target can look like — native values no real volume reports, and a volume reporting a component
/// length of zero. Producing those for real would need a network share, a quota or a hand-made volume.
/// Everything else is tested against a real directory.
/// </para>
/// <para>
/// The fake answers with the same shape the seam promises: a boolean outcome, the reported values, and
/// the native error code where there is one. It also records which paths it was asked about, so a test
/// can prove the probe asked the right question rather than only reading the answer.
/// </para>
/// </remarks>
internal sealed class ScriptedFileSystemApi : IWindowsFileSystemApi
{
    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorBadNetName = 67;
    internal const int ErrorInvalidName = 123;

    internal const ulong DefaultFreeBytesAvailableToCaller = 12_345_678_901;
    internal const string DefaultVolumePath = @"C:\";
    internal const string DefaultFileSystemName = "NTFS";
    internal const uint DefaultMaximumComponentLength = 255;

    private bool _attributesFail;
    private int _attributesError;
    private FileAttributes _attributes = FileAttributes.Directory;

    private bool _freeSpaceFails;
    private int _freeSpaceError;
    private ulong _freeBytesAvailableToCaller = DefaultFreeBytesAvailableToCaller;

    private bool _volumePathFails;
    private int _volumePathError;
    private string _volumePath = DefaultVolumePath;

    private bool _volumeInformationFails;
    private int _volumeInformationError;
    private string _fileSystemName = DefaultFileSystemName;
    private uint _maximumComponentLength = DefaultMaximumComponentLength;

    private readonly List<string> _attributePaths = [];
    private readonly List<string> _freeSpacePaths = [];
    private readonly List<string> _volumePathPaths = [];
    private readonly List<string> _volumeInformationPaths = [];

    /// <summary>Paths the probe asked to inspect, in the order it asked.</summary>
    internal IReadOnlyList<string> AttributePaths => _attributePaths;

    /// <summary>Paths the probe asked the free-space question about, in the order it asked.</summary>
    internal IReadOnlyList<string> FreeSpacePaths => _freeSpacePaths;

    /// <summary>Paths the probe asked to resolve to a volume, in the order it asked.</summary>
    internal IReadOnlyList<string> VolumePathPaths => _volumePathPaths;

    /// <summary>Paths the probe asked to describe as a volume root, in the order it asked.</summary>
    internal IReadOnlyList<string> VolumeInformationPaths => _volumeInformationPaths;

    /// <summary>Reports the given attributes for any path asked about.</summary>
    internal ScriptedFileSystemApi WithAttributes(FileAttributes attributes)
    {
        _attributesFail = false;
        _attributes = attributes;
        return this;
    }

    /// <summary>Reports that no attributes could be read, with the given native error.</summary>
    internal ScriptedFileSystemApi WithAttributeFailure(int nativeError)
    {
        _attributesFail = true;
        _attributesError = nativeError;
        return this;
    }

    internal ScriptedFileSystemApi WithFreeBytesAvailableToCaller(ulong freeBytes)
    {
        _freeSpaceFails = false;
        _freeBytesAvailableToCaller = freeBytes;
        return this;
    }

    internal ScriptedFileSystemApi WithFreeSpaceFailure(int nativeError)
    {
        _freeSpaceFails = true;
        _freeSpaceError = nativeError;
        return this;
    }

    internal ScriptedFileSystemApi WithVolumePath(string volumePathName)
    {
        _volumePathFails = false;
        _volumePath = volumePathName;
        return this;
    }

    /// <summary>
    /// Reports that the volume path query answered nothing. A native error of <c>0</c> models a call
    /// that succeeded but reported no usable path; a non-zero code models a call that failed.
    /// </summary>
    internal ScriptedFileSystemApi WithVolumePathFailure(int nativeError)
    {
        _volumePathFails = true;
        _volumePathError = nativeError;
        return this;
    }

    internal ScriptedFileSystemApi WithVolumeInformation(string fileSystemName, uint maximumComponentLength)
    {
        _volumeInformationFails = false;
        _fileSystemName = fileSystemName;
        _maximumComponentLength = maximumComponentLength;
        return this;
    }

    internal ScriptedFileSystemApi WithVolumeInformationFailure(int nativeError)
    {
        _volumeInformationFails = true;
        _volumeInformationError = nativeError;
        return this;
    }

    /// <inheritdoc />
    public bool TryGetFileAttributes(string path, out FileAttributes attributes, out int nativeError)
    {
        _attributePaths.Add(path);

        attributes = _attributesFail ? default : _attributes;
        nativeError = _attributesFail ? _attributesError : 0;
        return !_attributesFail;
    }

    /// <inheritdoc />
    public bool TryGetFreeBytesAvailableToCaller(
        string directoryPath,
        out ulong freeBytesAvailableToCaller,
        out int nativeError)
    {
        _freeSpacePaths.Add(directoryPath);

        freeBytesAvailableToCaller = _freeSpaceFails ? 0 : _freeBytesAvailableToCaller;
        nativeError = _freeSpaceFails ? _freeSpaceError : 0;
        return !_freeSpaceFails;
    }

    /// <inheritdoc />
    public bool TryGetVolumePathName(string path, out string volumePathName, out int nativeError)
    {
        _volumePathPaths.Add(path);

        volumePathName = _volumePathFails ? string.Empty : _volumePath;
        nativeError = _volumePathFails ? _volumePathError : 0;
        return !_volumePathFails;
    }

    /// <inheritdoc />
    public bool TryGetVolumeInformation(
        string volumeRootPath,
        out string fileSystemName,
        out uint maximumComponentLength,
        out int nativeError)
    {
        _volumeInformationPaths.Add(volumeRootPath);

        fileSystemName = _volumeInformationFails ? string.Empty : _fileSystemName;
        maximumComponentLength = _volumeInformationFails ? 0 : _maximumComponentLength;
        nativeError = _volumeInformationFails ? _volumeInformationError : 0;
        return !_volumeInformationFails;
    }
}

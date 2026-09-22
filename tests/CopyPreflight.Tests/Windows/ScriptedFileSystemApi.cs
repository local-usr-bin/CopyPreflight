using System.IO;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Supplies the Windows file-system seams with scripted answers to the queries they make.
/// </summary>
/// <remarks>
/// <para>
/// Used only for the outcomes a developer machine cannot be made to produce honestly: a volume whose
/// information query fails while its free-space query succeeds — which is what an ordinary SMB or NAS
/// target can look like — native values no real volume reports, a volume reporting a component length
/// of zero, and the attribute outcomes that depend on rights, on a network share or on a specific
/// reparse point. Producing those for real would need a network share, a quota or a hand-made volume.
/// Everything else is tested against a real directory.
/// </para>
/// <para>
/// The fake answers with the same shape the seams promise: a boolean outcome, the reported values, and
/// the native error code where there is one. It also records which paths it was asked about, so a test
/// can prove a probe asked the right question rather than only reading the answer.
/// </para>
/// <para>
/// Answers can be scripted per path as well as as a blanket default. The path-keyed answers are what
/// the mapped-location check needs, because the whole point of that check is that different paths in one
/// mapping answer differently. Path keys are matched ordinally, which is also how the check itself has
/// to remember locations.
/// </para>
/// </remarks>
internal sealed class ScriptedFileSystemApi : IWindowsFileSystemApi
{
    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorSharingViolation = 32;
    internal const int ErrorNotSupported = 50;

    /// <summary>
    /// The error <c>GetFileAttributesW</c> is documented to report when it is pointed straight at a
    /// network share root, where it requires a subdirectory instead.
    /// </summary>
    internal const int ErrorBadNetPath = 53;

    internal const int ErrorBadNetName = 67;
    internal const int ErrorInvalidName = 123;

    /// <summary>The error reported when a path is longer than the form it was queried in allows.</summary>
    internal const int ErrorFilenameExceededRange = 206;

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

    private readonly Dictionary<string, ScriptedAttributeAnswer> _attributeAnswers = new(StringComparer.Ordinal);

    private bool _rejectUnscriptedAttributePaths;

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

    /// <summary>Reports the given attributes for exactly one path, whatever the default is.</summary>
    internal ScriptedFileSystemApi WithAttribute(string path, FileAttributes attributes)
    {
        _attributeAnswers[path] = new ScriptedAttributeAnswer(attributes, NativeError: 0, Failed: false);
        return this;
    }

    /// <summary>Reports that one path could not be inspected, with the given native error.</summary>
    internal ScriptedFileSystemApi WithAttributeFailureFor(string path, int nativeError)
    {
        _attributeAnswers[path] = new ScriptedAttributeAnswer(default, nativeError, Failed: true);
        return this;
    }

    /// <summary>Reports the given attributes for every path that has no scripted answer of its own.</summary>
    internal ScriptedFileSystemApi WithUnscriptedPathsAnswering(FileAttributes attributes)
        => WithAttributes(attributes);

    /// <summary>
    /// Refuses to answer for a path no test scripted, instead of falling back to the default.
    /// </summary>
    /// <remarks>
    /// A quiet fallback would let a test pass while the probe asked about a path the test never
    /// intended it to ask about, which is exactly the kind of mistake these tests exist to catch. A
    /// path that was not scripted is therefore a test defect, and it is reported as one.
    /// </remarks>
    internal ScriptedFileSystemApi RequiringScriptedAttributePaths()
    {
        _rejectUnscriptedAttributePaths = true;
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

        if (_attributeAnswers.TryGetValue(path, out var scripted))
        {
            attributes = scripted.Attributes;
            nativeError = scripted.NativeError;
            return !scripted.Failed;
        }

        if (_rejectUnscriptedAttributePaths)
        {
            throw new InvalidOperationException(
                $"The scripted file-system API was asked about the path '{path}', for which the test "
                + "scripted no answer. Either script that path or stop requiring scripted paths.");
        }

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

    /// <summary>The answer one path was scripted with.</summary>
    private readonly record struct ScriptedAttributeAnswer(
        FileAttributes Attributes,
        int NativeError,
        bool Failed);
}

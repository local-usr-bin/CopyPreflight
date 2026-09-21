using System.IO;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Records every progress snapshot synchronously, on the calling thread.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> is deliberately not used in tests: it posts to a captured
/// synchronization context, so its callbacks can run after the scan has already returned and its
/// arrival order is not something a test should rely on.
/// </remarks>
internal sealed class RecordingProgress : IProgress<SourceScanProgress>
{
    private readonly Action<SourceScanProgress>? _onReport;
    private readonly List<SourceScanProgress> _snapshots = [];

    internal RecordingProgress(Action<SourceScanProgress>? onReport = null) => _onReport = onReport;

    internal IReadOnlyList<SourceScanProgress> Snapshots => _snapshots;

    public void Report(SourceScanProgress value)
    {
        _snapshots.Add(value);
        _onReport?.Invoke(value);
    }
}

/// <summary>
/// Supplies the scanner with fabricated directory listings, one directory at a time.
/// </summary>
/// <remarks>
/// Used only for the two situations a test cannot produce honestly with ordinary file-system calls:
/// a listing that fails, and a reparse point shaped like a file. Everything else is tested against a
/// real temporary tree.
/// </remarks>
internal sealed class ScriptedDirectoryReader : ISourceDirectoryReader
{
    private readonly Dictionary<string, Func<IEnumerable<ObservedDirectoryChild>>> _listings =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _listedDirectories = [];

    /// <summary>Absolute paths the scanner asked to list, in the order it asked.</summary>
    internal IReadOnlyList<string> ListedDirectories => _listedDirectories;

    internal ScriptedDirectoryReader WithChildren(
        string absoluteDirectoryPath,
        params ObservedDirectoryChild[] children)
        => WithListing(absoluteDirectoryPath, () => children);

    internal ScriptedDirectoryReader WithListing(
        string absoluteDirectoryPath,
        Func<IEnumerable<ObservedDirectoryChild>> listing)
    {
        _listings[absoluteDirectoryPath] = listing;
        return this;
    }

    internal ScriptedDirectoryReader WithFailure(string absoluteDirectoryPath, Exception failure)
        => WithListing(absoluteDirectoryPath, () => throw failure);

    public IEnumerable<ObservedDirectoryChild> ReadChildren(string absoluteDirectoryPath)
    {
        _listedDirectories.Add(absoluteDirectoryPath);

        return _listings.TryGetValue(absoluteDirectoryPath, out var listing)
            ? listing()
            : [];
    }
}

/// <summary>Builds the fabricated observations used by the scripted reader.</summary>
internal static class Observations
{
    internal static ObservedDirectoryChild FileItem(string name, long lengthInBytes)
        => new(name, FileAttributes.Archive, lengthInBytes, MetadataFailure: null);

    /// <summary>A file that was listed but whose length was never established.</summary>
    internal static ObservedDirectoryChild FileItemWithoutLength(string name)
        => new(name, FileAttributes.Archive, LengthInBytes: null, MetadataFailure: null);

    internal static ObservedDirectoryChild DirectoryItem(string name)
        => new(name, FileAttributes.Directory, LengthInBytes: null, MetadataFailure: null);

    /// <summary>
    /// A reparse point, of either shape, whose reported length deliberately does not match zero so a
    /// test can prove nothing is attributed from it.
    /// </summary>
    internal static ObservedDirectoryChild ReparsePointItem(string name, bool directoryShaped)
        => new(
            name,
            directoryShaped
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Archive | FileAttributes.ReparsePoint,
            directoryShaped ? null : 4096L,
            MetadataFailure: null);

    internal static ObservedDirectoryChild UnreadableItem(string name, Exception failure)
        => new(name, Attributes: null, LengthInBytes: null, failure);

    internal static ObservedDirectoryChild ItemWithoutMetadata(string name)
        => new(name, Attributes: null, LengthInBytes: null, MetadataFailure: null);
}

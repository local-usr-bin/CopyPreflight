namespace CopyPreflight.Core;

/// <summary>
/// The minimal, immutable fact model for one source object the scanner successfully observed.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SourceEntry"/> only states <i>"the scanner observed these facts about this
/// object"</i>. It does not judge what those facts mean for the target, and it performs no I/O.
/// </para>
/// <para>
/// The path is kept as the raw relative path that was observed. Path normalization, target path
/// composition, maximum-length checks and file-name validity are rule-stage concerns and are
/// deliberately not implemented here.
/// </para>
/// <para>
/// More facts (reparse point classification, readability, timestamps, ACLs, …) are added
/// incrementally by later milestones; they are not pre-declared now.
/// </para>
/// </remarks>
public sealed record SourceEntry
{
    private SourceEntry(string relativePath, SourceEntryKind kind, long? lengthInBytes)
    {
        RelativePath = relativePath;
        Kind = kind;
        LengthInBytes = lengthInBytes;
    }

    /// <summary>Describes an observed file, including its logical length.</summary>
    /// <param name="relativePath">Path of the entry relative to the source root.</param>
    /// <param name="lengthInBytes">Logical length of the file in bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lengthInBytes"/> is negative.</exception>
    public static SourceEntry File(string relativePath, long lengthInBytes)
    {
        if (lengthInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lengthInBytes),
                lengthInBytes,
                "A file length cannot be negative.");
        }

        return new SourceEntry(RequireRelativePath(relativePath), SourceEntryKind.File, lengthInBytes);
    }

    /// <summary>
    /// Describes an observed directory. A directory has no file length, and none is invented for it.
    /// </summary>
    /// <param name="relativePath">Path of the entry relative to the source root.</param>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is null, empty or whitespace.</exception>
    public static SourceEntry Directory(string relativePath)
        => new(RequireRelativePath(relativePath), SourceEntryKind.Directory, lengthInBytes: null);

    /// <summary>Path of the entry relative to the source root, exactly as observed.</summary>
    public string RelativePath { get; }

    /// <summary>Whether this entry is a file or a directory.</summary>
    public SourceEntryKind Kind { get; }

    /// <summary>
    /// Logical length in bytes. Non-null if and only if <see cref="Kind"/> is
    /// <see cref="SourceEntryKind.File"/>; <see langword="null"/> for directories, which have no
    /// file length.
    /// </summary>
    public long? LengthInBytes { get; }

    private static string RequireRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException(
                "A source entry path must not be null, empty or whitespace.",
                nameof(relativePath));
        }

        return relativePath;
    }
}

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
/// Instances are created only through <see cref="File"/>, <see cref="Directory"/> or
/// <see cref="ReparsePoint"/>. Every property is read-only, so the invariants below cannot be
/// bypassed by an object initializer or a <c>with</c> expression.
/// </para>
/// <para>
/// More facts (reparse tag classification, readability, timestamps, ACLs, …) are added
/// incrementally by later milestones; they are not pre-declared now.
/// </para>
/// </remarks>
public sealed record SourceEntry
{
    private SourceEntry(
        string relativePath,
        SourceEntryKind kind,
        long? lengthInBytes,
        bool isReparsePoint)
    {
        RelativePath = relativePath;
        Kind = kind;
        LengthInBytes = lengthInBytes;
        IsReparsePoint = isReparsePoint;
    }

    /// <summary>Describes an observed regular file, including its logical length.</summary>
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

        return new SourceEntry(
            RequireRelativePath(relativePath),
            SourceEntryKind.File,
            lengthInBytes,
            isReparsePoint: false);
    }

    /// <summary>
    /// Describes an observed regular directory. A directory has no file length, and none is
    /// invented for it.
    /// </summary>
    /// <param name="relativePath">Path of the entry relative to the source root.</param>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is null, empty or whitespace.</exception>
    public static SourceEntry Directory(string relativePath)
        => new(
            RequireRelativePath(relativePath),
            SourceEntryKind.Directory,
            lengthInBytes: null,
            isReparsePoint: false);

    /// <summary>
    /// Describes an observed object that carries the reparse point attribute.
    /// </summary>
    /// <param name="relativePath">Path of the entry relative to the source root.</param>
    /// <param name="kind">
    /// Whether the object is shaped like a file or like a directory, as reported by the file
    /// system. It is <b>not</b> a classification of the reparse point.
    /// </param>
    /// <returns>
    /// An entry whose <see cref="IsReparsePoint"/> is <see langword="true"/> and whose
    /// <see cref="LengthInBytes"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    /// <remarks>
    /// No logical length is attributed to a reparse point. The length a file system reports for
    /// such an object may describe the link target rather than the object itself, and telling the
    /// two apart requires the reparse tag classification that is deliberately deferred to a later
    /// milestone. Reporting <c>0</c>, or reporting the reported number as if it were this object's
    /// own length, would both state a fact the scanner has not established. An unattributed length
    /// is therefore reported as an absent one.
    /// </remarks>
    public static SourceEntry ReparsePoint(string relativePath, SourceEntryKind kind)
    {
        if (kind is not (SourceEntryKind.File or SourceEntryKind.Directory))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A reparse point entry must be shaped like a file or like a directory.");
        }

        return new SourceEntry(
            RequireRelativePath(relativePath),
            kind,
            lengthInBytes: null,
            isReparsePoint: true);
    }

    /// <summary>Path of the entry relative to the source root, exactly as observed.</summary>
    public string RelativePath { get; }

    /// <summary>
    /// Whether this entry is shaped like a file or like a directory. For a reparse point this is
    /// the shape reported by the file system, not a classification of the reparse point itself.
    /// </summary>
    public SourceEntryKind Kind { get; }

    /// <summary>
    /// Logical length in bytes, attributed only when the scanner could honestly attribute one.
    /// Non-null if and only if <see cref="Kind"/> is <see cref="SourceEntryKind.File"/> and
    /// <see cref="IsReparsePoint"/> is <see langword="false"/>; <see langword="null"/> for
    /// directories and for reparse points, neither of which carries an attributed file length.
    /// </summary>
    public long? LengthInBytes { get; }

    /// <summary>
    /// Whether the observed object carries the <see cref="System.IO.FileAttributes.ReparsePoint"/>
    /// attribute.
    /// </summary>
    /// <remarks>
    /// This states only that a reparse point was encountered. Which kind of reparse point it is —
    /// symbolic link, junction, mount point or something else — is deliberately not classified in
    /// this milestone, and nothing about the link target is recorded.
    /// </remarks>
    public bool IsReparsePoint { get; }

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

namespace CopyPreflight.Core;

/// <summary>
/// Everything one completed scan of a source root observed.
/// </summary>
/// <remarks>
/// <para>
/// The result is a plain, inspectable value: the entries that were observed, the issues that
/// prevented part of the tree from being observed, and counters derived from those entries. It is
/// produced by <see cref="SourceScanner"/> and performs no I/O of its own.
/// </para>
/// <para>
/// The counters are computed from <see cref="Entries"/>, so a summary can never disagree with the
/// inventory it summarises. They are disjoint and exhaustive:
/// <see cref="FileCount"/> + <see cref="DirectoryCount"/> + <see cref="ReparsePointCount"/> always
/// equals <see cref="Entries"/><c>.Count</c>, which is why reparse points are counted separately
/// rather than folded into the file and directory totals.
/// </para>
/// <para>
/// The source root itself is never an entry and never counted as a directory.
/// </para>
/// </remarks>
public sealed class SourceScanResult
{
    /// <summary>Creates a scan result and derives its counters from <paramref name="entries"/>.</summary>
    /// <param name="sourceRoot">The resolved absolute path that was scanned.</param>
    /// <param name="entries">The entries that were observed, in observation order.</param>
    /// <param name="issues">The recoverable failures recorded during the scan.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceRoot"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entries"/> or <paramref name="issues"/> is <see langword="null"/>.
    /// </exception>
    public SourceScanResult(
        string sourceRoot,
        IReadOnlyList<SourceEntry> entries,
        IReadOnlyList<SourceScanIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(issues);

        SourceRoot = string.IsNullOrWhiteSpace(sourceRoot)
            ? throw new ArgumentException(
                "A source root must not be null, empty or whitespace.",
                nameof(sourceRoot))
            : sourceRoot;

        Entries = entries;
        Issues = issues;

        long fileCount = 0;
        long directoryCount = 0;
        long reparsePointCount = 0;
        long logicalFileBytes = 0;

        foreach (var entry in entries)
        {
            if (entry.IsReparsePoint)
            {
                reparsePointCount++;
            }
            else if (entry.Kind == SourceEntryKind.File)
            {
                fileCount++;
                logicalFileBytes += entry.LengthInBytes!.Value;
            }
            else
            {
                directoryCount++;
            }
        }

        FileCount = fileCount;
        DirectoryCount = directoryCount;
        ReparsePointCount = reparsePointCount;
        LogicalFileBytes = logicalFileBytes;
    }

    /// <summary>
    /// The resolved absolute path of the directory that was scanned. The source root itself is
    /// never an entry, so it appears only here.
    /// </summary>
    public string SourceRoot { get; }

    /// <summary>
    /// The entries observed in the source tree, in observation order. Order is an artifact of file
    /// system enumeration and carries no meaning.
    /// </summary>
    public IReadOnlyList<SourceEntry> Entries { get; }

    /// <summary>
    /// The recoverable failures recorded while observing. Empty when the scan observed everything
    /// it set out to observe.
    /// </summary>
    public IReadOnlyList<SourceScanIssue> Issues { get; }

    /// <summary>Number of regular files observed. Reparse points are not counted here.</summary>
    public long FileCount { get; }

    /// <summary>
    /// Number of regular directories observed. The source root itself is never counted, and
    /// reparse points are not counted here.
    /// </summary>
    public long DirectoryCount { get; }

    /// <summary>
    /// Number of reparse points observed, whether shaped like a file or like a directory. They are
    /// counted separately because they are not yet classified and carry no attributed length.
    /// </summary>
    public long ReparsePointCount { get; }

    /// <summary>
    /// Sum of the logical lengths attributed to regular files. Reparse points carry no attributed
    /// length, so they contribute nothing; a source tree made only of reparse points yields
    /// <c>0</c> here while <see cref="ReparsePointCount"/> is non-zero.
    /// </summary>
    public long LogicalFileBytes { get; }

    /// <summary>
    /// Whether every part of the tree the scanner attempted to observe could actually be observed.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> means no recoverable failure was recorded: every directory that was
    /// reached could be listed, and the metadata of every object seen in those listings could be
    /// read. It says nothing about whether the file contents can be read — M2 does not open any
    /// file — and nothing about whether a copy would succeed.
    /// </remarks>
    public bool IsComplete => Issues.Count == 0;
}

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
/// <para>
/// <b>A root that could not be used is an outcome of its own, not an empty inventory.</b> When the
/// scanner refuses a root before any traversal, there is nothing to observe and nothing was
/// observed; that is what <see cref="UnusableRootReason"/> records, and it is why
/// <see cref="IsComplete"/> is <see langword="false"/> for such a result even though no
/// <see cref="SourceScanIssue"/> was recorded. The zeroes in the counters are then not observations:
/// a caller that shows them must show them as "nothing was observed" rather than as counts of zero,
/// which is a claim this result does not support.
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
    /// <remarks>
    /// This is the constructor for a scan that <i>ran</i>. A root the scanner refused before it
    /// started is expressed by <see cref="ForUnusableRoot"/> instead, which records that refusal as
    /// the fact it is.
    /// </remarks>
    public SourceScanResult(
        string sourceRoot,
        IReadOnlyList<SourceEntry> entries,
        IReadOnlyList<SourceScanIssue> issues)
        : this(sourceRoot, entries, issues, unusableRootReason: null)
    {
    }

    /// <summary>Creates a scan result from every fact it holds, including a refused root.</summary>
    private SourceScanResult(
        string sourceRoot,
        IReadOnlyList<SourceEntry> entries,
        IReadOnlyList<SourceScanIssue> issues,
        InvalidSourceRootReason? unusableRootReason)
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
        UnusableRootReason = unusableRootReason;

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
    /// Expresses a source root the scanner refused before it started observing as the outcome it is.
    /// </summary>
    /// <param name="rootFailure">
    /// The precondition failure <see cref="SourceScanner.Scan"/> reported for the root.
    /// </param>
    /// <returns>
    /// A result carrying the resolved root, no entries, no issues, and the reason the root could not
    /// be used. It is deliberately <b>not</b> an inventory: nothing was traversed, so no entry was
    /// observed and no count here is an observation.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rootFailure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The scanner keeps a precondition failure apart from a recoverable scan issue, and this factory
    /// keeps that distinction while giving the caller something it can carry and report. It observes
    /// nothing, opens nothing and re-resolves nothing: it states the refusal the scanner already
    /// decided, using the path the scanner resolved.
    /// </remarks>
    public static SourceScanResult ForUnusableRoot(InvalidSourceRootException rootFailure)
    {
        ArgumentNullException.ThrowIfNull(rootFailure);

        return new SourceScanResult(
            rootFailure.ResolvedSourceRoot,
            [],
            [],
            rootFailure.Reason);
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
    /// Why the source root could not be used at all, or <see langword="null"/> when the root was
    /// usable and the scan ran.
    /// </summary>
    /// <remarks>
    /// It is present only for a result built by <see cref="ForUnusableRoot"/>. It states a
    /// precondition fact about the input — the root is not there, or it is not a directory — rather
    /// than a failure that happened while observing, which is why it is not a
    /// <see cref="SourceScanIssue"/>.
    /// </remarks>
    public InvalidSourceRootReason? UnusableRootReason { get; }

    /// <summary>
    /// Whether the source root was usable, so that <see cref="Entries"/> and <see cref="Issues"/> are
    /// the outcome of an observation that actually ran.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="false"/> nothing was traversed: the counters describe nothing that
    /// was seen, and they must be presented as "nothing was observed" rather than as zeroes.
    /// </remarks>
    public bool HasUsableSourceRoot => UnusableRootReason is null;

    /// <summary>
    /// Whether every part of the tree the scanner attempted to observe could actually be observed.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> means no recoverable failure was recorded and the root itself was
    /// usable: every directory that was reached could be listed, and the metadata of every object
    /// seen in those listings could be read. A root that could not be used is never complete, even
    /// though it records no issue, because no observation happened at all. It says nothing about
    /// whether the file contents can be read — M2 does not open any file — and nothing about whether
    /// a copy would succeed.
    /// </remarks>
    public bool IsComplete => Issues.Count == 0 && HasUsableSourceRoot;
}

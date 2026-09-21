namespace CopyPreflight.Core;

/// <summary>
/// A monotonic snapshot of how far a running scan has got.
/// </summary>
/// <remarks>
/// <para>
/// The counters are counts of what has been <b>observed so far</b>. They deliberately carry no
/// percentage and no completion estimate: a source tree has no reliably known total up front, and
/// discovering one would require a counting pass that this milestone does not perform. Inventing a
/// percentage from an unknown denominator would be a fabricated fact.
/// </para>
/// <para>
/// The counters are non-negative and never decrease while a scan runs. A scan always reports at
/// least once, and the final snapshot is equal to the counters of the returned
/// <see cref="SourceScanResult"/>.
/// </para>
/// <para>
/// Progress is reported at directory granularity — once per successfully walked directory — so the
/// number of callbacks is bounded by the number of directories rather than the number of entries.
/// A snapshot is therefore a coarse "where am I" signal, not a per-entry event stream.
/// </para>
/// </remarks>
public sealed record SourceScanProgress
{
    /// <summary>Creates a progress snapshot.</summary>
    /// <param name="entriesObserved">Total entries observed so far.</param>
    /// <param name="filesObserved">Regular files observed so far.</param>
    /// <param name="directoriesObserved">Regular directories observed so far, excluding the source root.</param>
    /// <param name="reparsePointsObserved">Reparse points observed so far, of either shape.</param>
    /// <param name="logicalBytesObserved">Sum of the logical file lengths attributed so far.</param>
    /// <param name="issuesObserved">Scan issues recorded so far.</param>
    /// <param name="currentRelativePath">
    /// Relative path of the directory whose children were most recently observed, or
    /// <see langword="null"/> while working at the source root itself.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any counter is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="currentRelativePath"/> is empty or whitespace.</exception>
    public SourceScanProgress(
        long entriesObserved,
        long filesObserved,
        long directoriesObserved,
        long reparsePointsObserved,
        long logicalBytesObserved,
        long issuesObserved,
        string? currentRelativePath)
    {
        EntriesObserved = RequireNonNegative(entriesObserved, nameof(entriesObserved));
        FilesObserved = RequireNonNegative(filesObserved, nameof(filesObserved));
        DirectoriesObserved = RequireNonNegative(directoriesObserved, nameof(directoriesObserved));
        ReparsePointsObserved = RequireNonNegative(reparsePointsObserved, nameof(reparsePointsObserved));
        LogicalBytesObserved = RequireNonNegative(logicalBytesObserved, nameof(logicalBytesObserved));
        IssuesObserved = RequireNonNegative(issuesObserved, nameof(issuesObserved));
        CurrentRelativePath = currentRelativePath is null
            ? null
            : RequireNonNegativeText(currentRelativePath, nameof(currentRelativePath));
    }

    /// <summary>Total entries observed so far.</summary>
    public long EntriesObserved { get; }

    /// <summary>Regular files observed so far.</summary>
    public long FilesObserved { get; }

    /// <summary>Regular directories observed so far. The source root itself is never counted.</summary>
    public long DirectoriesObserved { get; }

    /// <summary>Reparse points observed so far, whether shaped like a file or like a directory.</summary>
    public long ReparsePointsObserved { get; }

    /// <summary>Sum of the logical file lengths attributed so far.</summary>
    public long LogicalBytesObserved { get; }

    /// <summary>Scan issues recorded so far.</summary>
    public long IssuesObserved { get; }

    /// <summary>
    /// Relative path of the directory whose children were most recently observed, or
    /// <see langword="null"/> while working at the source root itself.
    /// </summary>
    public string? CurrentRelativePath { get; }

    private static long RequireNonNegative(long value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A progress counter cannot be negative.");

    private static string RequireNonNegativeText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A relative path must not be empty or whitespace.", parameterName)
            : value;
}

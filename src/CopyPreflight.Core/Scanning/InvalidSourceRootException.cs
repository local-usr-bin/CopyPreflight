namespace CopyPreflight.Core;

/// <summary>
/// Why a source root was rejected before any scanning started.
/// </summary>
public enum InvalidSourceRootReason
{
    /// <summary>Nothing exists at the resolved path.</summary>
    NotFound = 0,

    /// <summary>The resolved path exists but is not a directory.</summary>
    NotADirectory = 1,
}

/// <summary>
/// Thrown when the supplied source root cannot be used as the root of a scan.
/// </summary>
/// <remarks>
/// <para>
/// This is a precondition failure, reported before any traversal begins, not a recoverable scan
/// issue: with an unusable root there is nothing to observe at all, so no partial result is
/// produced. It is deliberately distinct from <see cref="SourceScanIssue"/>, which records
/// failures that occur <i>during</i> a scan of an otherwise usable root.
/// </para>
/// <para>
/// A directory that exists but cannot be enumerated is <b>not</b> reported through this type. Such
/// a root is usable, so the scan proceeds and records a
/// <see cref="SourceScanIssueKind.CannotEnumerateDirectory"/> issue instead, leaving the caller
/// with an explicit partial result rather than a failure.
/// </para>
/// <para>
/// A rejection is an <b>expected outcome of the input</b>, not a defect of the application, so it is
/// carried as a fact a caller can report instead of as something it has to hide. The separate
/// <see cref="Reason"/> is what keeps the two admissible cases apart: a root that is not there and a
/// root that is not a directory are different facts, and reporting either one as the other would be
/// a claim the scanner never established.
/// </para>
/// </remarks>
public sealed class InvalidSourceRootException : Exception
{
    /// <summary>Creates the exception for a source root that cannot be used.</summary>
    /// <param name="sourceRoot">The source root as supplied by the caller.</param>
    /// <param name="resolvedSourceRoot">
    /// The path the rejection was decided on, resolved to an absolute path by the same resolution the
    /// scanner would have used for a root it could walk.
    /// </param>
    /// <param name="reason">Why the source root was rejected.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sourceRoot"/> or <paramref name="resolvedSourceRoot"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="reason"/> is not a known reason.
    /// </exception>
    public InvalidSourceRootException(
        string sourceRoot,
        string resolvedSourceRoot,
        InvalidSourceRootReason reason)
        : base(BuildMessage(sourceRoot, reason))
    {
        ArgumentNullException.ThrowIfNull(sourceRoot);
        ArgumentNullException.ThrowIfNull(resolvedSourceRoot);

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown source root reason.");
        }

        SourceRoot = sourceRoot;
        ResolvedSourceRoot = resolvedSourceRoot;
        Reason = reason;
    }

    /// <summary>The source root exactly as supplied by the caller, before path resolution.</summary>
    public string SourceRoot { get; }

    /// <summary>
    /// The path the rejection was decided on, resolved to an absolute path. It is the path that would
    /// have been walked, so a report can name the location that was actually rejected without
    /// resolving the path a second time.
    /// </summary>
    public string ResolvedSourceRoot { get; }

    /// <summary>Why the source root was rejected.</summary>
    public InvalidSourceRootReason Reason { get; }

    private static string BuildMessage(string sourceRoot, InvalidSourceRootReason reason)
        => reason switch
        {
            InvalidSourceRootReason.NotFound =>
                $"The source root '{sourceRoot}' does not exist, so there is nothing to scan.",
            InvalidSourceRootReason.NotADirectory =>
                $"The source root '{sourceRoot}' is not a directory, so there is nothing to scan.",
            _ => $"The source root '{sourceRoot}' cannot be used as the root of a scan.",
        };
}

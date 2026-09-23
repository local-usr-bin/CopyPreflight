using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Turns a source root that could not be used at all into the source-side blocking finding it is.
/// </summary>
/// <remarks>
/// <para>
/// A root nothing exists at, or a root that is not a directory, is an <b>expected outcome of the
/// input</b> rather than a defect of the application. It is nevertheless the most fundamental thing
/// this check can report, because nothing else it says about the source depends on anything but
/// that tree: with an unusable root there is no inventory, so no source-dependent rule has anything
/// to reason about. It is therefore an Error, and it is stated as a conclusion rather than shown as
/// a crash.
/// </para>
/// <para>
/// The wording keeps the two admissible cases apart. "Does not exist" and "is not a directory" are
/// different facts, and each is stated only when the scanner established it; neither is offered as a
/// general explanation of a root that could not be used. Nothing here says "does not exist" on the
/// strength of a check that cannot tell absence from inaccessibility, and no permission, network or
/// device failure is folded into either sentence.
/// </para>
/// <para>
/// The internal precondition exception is never named. Its type name, its namespace and its message
/// belong to the diagnostic side of the application, not to a report about a folder, and a user who
/// typed a path that is not there is not being told about a class.
/// </para>
/// <para>
/// This rule consumes the fact the result already carries (<see cref="SourceScanResult.UnusableRootReason"/>)
/// and re-observes nothing: it opens no directory, resolves no path, and never creates the folder
/// that was missing.
/// </para>
/// </remarks>
internal static class SourceRootUnusableRule
{
    private const string NotFoundTitle = "Source directory does not exist";

    private const string NotFoundExplanation =
        "The source directory this check was asked to observe does not exist, so nothing in it could "
        + "be observed and no inventory of the source was produced.";

    private const string NotADirectoryTitle = "Source path is not a directory";

    private const string NotADirectoryExplanation =
        "The source path this check was asked to observe exists, but it is not a directory, so nothing "
        + "in it could be observed and no inventory of the source was produced.";

    private const string Impact =
        "No source-dependent conclusion could be drawn: path length, Windows naming, file system "
        + "limits and what the destination already holds would all have to describe contents that were "
        + "never observed.";

    private const string NotFoundRecommendation =
        "Check the source directory and run the preflight check again.";

    private const string NotADirectoryRecommendation =
        "Choose a source directory rather than a file, and run the preflight check again.";

    /// <summary>
    /// Produces the source-side finding for a root that could not be used, or nothing when the root
    /// was usable and the observation ran.
    /// </summary>
    /// <param name="source">What the source observation produced.</param>
    /// <returns>At most one finding, at most once per scan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.UnusableRootReason is not { } reason)
        {
            return [];
        }

        // The source root is not an entry, so this finding belongs to no relative path. Reporting one
        // would invent an entry that was never observed.
        return
        [
            new Finding(
                WindowsPreflightFindingCodes.SourceRootUnusable,
                Severity.Error,
                reason == InvalidSourceRootReason.NotADirectory ? NotADirectoryTitle : NotFoundTitle,
                reason == InvalidSourceRootReason.NotADirectory ? NotADirectoryExplanation : NotFoundExplanation,
                Impact,
                reason == InvalidSourceRootReason.NotADirectory
                    ? NotADirectoryRecommendation
                    : NotFoundRecommendation),
        ];
    }
}

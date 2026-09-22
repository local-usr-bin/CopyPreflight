using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Turns a target that was rejected as a destination into target-wide findings.
/// </summary>
/// <remarks>
/// <para>
/// Two probe issue kinds mean the target cannot serve as the destination root of a migration at all:
/// the path's name was refused by Windows, or no usable directory could be reached through it.
/// Either way the migration has nowhere to go, which is the one thing this milestone may call an
/// Error without qualification.
/// </para>
/// <para>
/// A target-wide finding carries no <see cref="Finding.SourceRelativePath"/>, because it applies to
/// the whole target rather than to any single source entry.
/// </para>
/// <para>
/// The other probe issue kinds — a volume query, a free-space query or a volume information query
/// that failed — are deliberately <b>not</b> mapped here. Each of those leaves exactly one
/// capability unknown, and the user is told about the resulting gap once, through that capability,
/// instead of being shown a Win32 error and then a capability gap for the same underlying fact.
/// </para>
/// </remarks>
internal static class TargetPathBlockerRule
{
    /// <summary>Whether the target was rejected outright as a destination.</summary>
    internal static bool IsTargetBlocked(TargetProbeResult target)
    {
        foreach (var issue in target.Issues)
        {
            if (issue.Kind is TargetProbeIssueKind.TargetPathInvalid or TargetProbeIssueKind.TargetPathUnavailable)
            {
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<Finding> Evaluate(TargetProbeResult target)
    {
        foreach (var issue in target.Issues)
        {
            switch (issue.Kind)
            {
                case TargetProbeIssueKind.TargetPathInvalid:
                    yield return CreateInvalidPathFinding(issue);
                    break;

                case TargetProbeIssueKind.TargetPathUnavailable:
                    yield return CreateUnavailablePathFinding(issue);
                    break;

                default:
                    // Not a blocking issue, so nothing is reported for it here; the capability it
                    // left unknown is reported as a coverage finding instead.
                    break;
            }
        }
    }

    private static Finding CreateInvalidPathFinding(TargetProbeIssue issue)
        => new(
            WindowsPreflightFindingCodes.TargetPathInvalid,
            Severity.Error,
            "Target path is not a valid Windows path",
            "Windows rejected the target path as a malformed name, so it cannot be used as the "
            + "destination directory of a migration."
            + DescribeEvidence(issue),
            "Nothing can be migrated into this target, and no target-dependent compatibility check "
            + "could be carried out.",
            "Choose a target directory whose path Windows accepts, then run the preflight check again.");

    private static Finding CreateUnavailablePathFinding(TargetProbeIssue issue)
        => new(
            WindowsPreflightFindingCodes.TargetPathUnavailable,
            Severity.Error,
            "Target directory is not available",
            "The target path is a well-formed name, but no usable directory could be reached through "
            + "it: it may not exist, may not be a directory, may be unreachable over the network, or "
            + "may be refused by permissions."
            + DescribeEvidence(issue),
            "Nothing can be migrated into this target, and no target-dependent compatibility check "
            + "could be carried out.",
            "Check that the target directory exists and can be reached by the current user, then run "
            + "the preflight check again.");

    /// <summary>Describes the failed query using evidence that does not depend on the display language.</summary>
    private static string DescribeEvidence(TargetProbeIssue issue)
        => issue.NativeErrorCode is { } nativeError
            ? $" (observed: {issue.Operation} reported Windows error {nativeError})"
            : $" (observed: {issue.Operation})";
}

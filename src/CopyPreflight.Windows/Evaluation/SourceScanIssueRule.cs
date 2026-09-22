using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Turns the scanner's recoverable observation failures into findings.
/// </summary>
/// <remarks>
/// <para>
/// Both scan issue kinds are Errors, because both leave the inventory incomplete: CopyPreflight
/// cannot honestly claim it has looked at the whole source data set, and every rule that reasons
/// about the source is only as trustworthy as the inventory it reads.
/// </para>
/// <para>
/// The wording deliberately keeps two different statements apart. "Part of the source could not be
/// inspected, so its migration risk cannot be assessed" is what was actually established;
/// "the copy of this object will fail" is <b>not</b>, and is never claimed here. A directory that
/// could not be listed may be perfectly copyable — CopyPreflight simply never saw what is in it.
/// </para>
/// <para>
/// The scan issue itself keeps no severity and no advice; that judgement is made here, in the rule
/// layer, exactly as the M2 contract requires.
/// </para>
/// </remarks>
internal static class SourceScanIssueRule
{
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source)
    {
        foreach (var issue in source.Issues)
        {
            // SourceScanIssue admits exactly these two kinds, so there is no third branch to take.
            yield return issue.Kind == SourceScanIssueKind.CannotEnumerateDirectory
                ? CreateEnumerationFinding(issue)
                : CreateMetadataFinding(issue);
        }
    }

    private static Finding CreateEnumerationFinding(SourceScanIssue issue)
        => new(
            WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed,
            Severity.Error,
            "Source directory could not be fully listed",
            "The contents of this directory could not be enumerated while the source was being "
            + "scanned, so CopyPreflight never observed what is inside it and the source inventory "
            + "is incomplete."
            + DescribeEvidence(issue),
            "Migration risk for whatever is inside this directory cannot be assessed reliably, "
            + "because none of it was observed. This does not mean that those objects would fail to "
            + "copy.",
            "Check that the current user can list this directory — for example its permissions, "
            + "whether it still exists, or whether the location is reachable — and run the preflight "
            + "check again.",
            sourceRelativePath: issue.RelativePath);

    private static Finding CreateMetadataFinding(SourceScanIssue issue)
        => new(
            WindowsPreflightFindingCodes.SourceEntryMetadataFailed,
            Severity.Error,
            "Source object could not be fully inspected",
            "This object was found inside a listed source directory, but the basic metadata needed "
            + "to describe it could not be read, so it is missing from the inventory."
            + DescribeEvidence(issue),
            "The migration risk of this object could not be assessed, because it was never fully "
            + "inspected. This does not mean that the object would fail to copy.",
            "Check that this object is still present and readable by the current user, then run the "
            + "preflight check again.",
            sourceRelativePath: issue.RelativePath);

    /// <summary>
    /// Describes the failure in terms that cannot change with the machine's language.
    /// </summary>
    /// <remarks>
    /// The exception's message is deliberately not used: it is localized text, and a rule layer
    /// whose output depends on the display language would produce different findings for the same
    /// observed facts. The exception type name and the <c>HResult</c> identify the failure just as
    /// precisely and stay stable.
    /// </remarks>
    private static string DescribeEvidence(SourceScanIssue issue)
    {
        if (issue.ExceptionTypeName is null && issue.HResult is null)
        {
            return string.Empty;
        }

        var kind = issue.ExceptionTypeName ?? "an unspecified failure";
        var code = issue.HResult is { } hResult ? $", HResult 0x{unchecked((uint)hResult):X8}" : string.Empty;

        return $" (observed failure: {kind}{code})";
    }
}

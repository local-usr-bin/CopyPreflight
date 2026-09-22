using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Reports, once per capability, that a check could not be carried out because the capability is
/// unknown.
/// </summary>
/// <remarks>
/// <para>
/// An unknown capability is not a problem with the target: it is a gap in what CopyPreflight was
/// able to observe, and it is reported as <see cref="Severity.Info"/> for exactly that reason. The
/// Unknown state is never turned into an Error here, and no default value is ever assumed in its
/// place.
/// </para>
/// <para>
/// Each gap produces at most one target-wide finding. Repeating it per source entry would add
/// thousands of identical lines to a report without telling the user anything more, so the finding
/// carries no <see cref="Finding.SourceRelativePath"/>.
/// </para>
/// <para>
/// This rule is only reached for a target that is usable as a directory. A target that was rejected
/// outright already has its own blocking Error, and piling three "this could not be checked" findings
/// on top of it would only add noise.
/// </para>
/// </remarks>
internal static class TargetCapabilityCoverageRule
{
    internal static IEnumerable<Finding> Evaluate(TargetCapabilities capabilities)
    {
        if (capabilities.FileSystemName.IsUnknown)
        {
            yield return CreateFileSystemUnknownFinding();
        }

        if (capabilities.MaxPathComponentLength.IsUnknown)
        {
            yield return CreateComponentLimitUnknownFinding();
        }

        if (capabilities.AvailableFreeBytes.IsUnknown)
        {
            yield return CreateFreeSpaceUnknownFinding();
        }
    }

    private static Finding CreateFileSystemUnknownFinding()
        => new(
            WindowsPreflightFindingCodes.TargetFileSystemUnknown,
            Severity.Info,
            "Target file system could not be determined",
            "Windows did not report which file system the target volume uses, so the name of the "
            + "target's file system is unknown. CopyPreflight does not guess one.",
            "File system specific checks could not be carried out — for example whether the target "
            + "can hold a file as large as the largest file in the source.",
            "Check the target volume directly if a file system limit matters for this migration; "
            + "everything else in this report is unaffected.");

    private static Finding CreateComponentLimitUnknownFinding()
        => new(
            WindowsPreflightFindingCodes.TargetComponentLimitUnknown,
            Severity.Info,
            "Target name length limit could not be determined",
            "Windows did not report the maximum length of a single file or directory name that the "
            + "target accepts, so that limit is unknown. No default limit is assumed in its place.",
            "Whether any source name is too long for the target could not be checked.",
            "Check the target file system's name length limit directly if it matters for this "
            + "migration; everything else in this report is unaffected.");

    private static Finding CreateFreeSpaceUnknownFinding()
        => new(
            WindowsPreflightFindingCodes.TargetFreeSpaceUnknown,
            Severity.Info,
            "Target free space could not be determined",
            "Windows did not report the space available to the current user on the target, so the "
            + "amount is unknown.",
            "The size of the source could not be compared with the target's available space, so no "
            + "space risk could be assessed.",
            "Check the target's available space directly if it matters for this migration; "
            + "everything else in this report is unaffected.");
}

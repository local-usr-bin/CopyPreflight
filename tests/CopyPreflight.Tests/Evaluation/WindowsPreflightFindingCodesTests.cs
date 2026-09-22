using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The finding codes are the stable identity of each rule, so they are pinned here: a rename or a
/// reuse would silently break anything that matches on them.
/// </summary>
public class WindowsPreflightFindingCodesTests
{
    private static readonly string[] ExpectedCodes =
    [
        "SOURCE_DIRECTORY_ENUMERATION_FAILED",
        "SOURCE_ENTRY_METADATA_FAILED",
        "TARGET_PATH_INVALID",
        "TARGET_PATH_UNAVAILABLE",
        "FAT32_FILE_TOO_LARGE",
        "TARGET_COMPONENT_TOO_LONG",
        "TRADITIONAL_PATH_LENGTH_RISK",
        "REPARSE_POINT_PRESENT",
        "TARGET_SPACE_MAY_BE_INSUFFICIENT",
        "TARGET_FILESYSTEM_UNKNOWN",
        "TARGET_COMPONENT_LIMIT_UNKNOWN",
        "TARGET_FREE_SPACE_UNKNOWN",
        "TARGET_NAME_INVALID_CHARACTER",
        "TARGET_NAME_RESERVED_DEVICE",
        "TARGET_NAME_TRAILING_SPACE_OR_PERIOD",
        "SOURCE_CASE_COLLISION_RISK",
        "TARGET_EXISTING_FILE_COLLISION",
        "TARGET_EXISTING_TYPE_COLLISION",
        "TARGET_EXISTING_REPARSE_POINT",
        "SOURCE_REPARSE_POINT_WITH_EXISTING_TARGET_OBJECT",
        "TARGET_EXISTING_OBJECT_CHECK_FAILED",
        "TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE",
        "TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED",
        "TARGET_EXISTING_OBJECT_CHECK_CANCELLED",
    ];

    [Fact]
    public void TheDeclaredCodesAreExactlyThePinnedSet()
    {
        Assert.Equal(
            ExpectedCodes.OrderBy(value => value, StringComparer.Ordinal),
            WindowsPreflightFindingCodes.All.Select(code => code.Value).OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void NoCodeIsDuplicated()
    {
        Assert.Equal(
            WindowsPreflightFindingCodes.All.Count,
            WindowsPreflightFindingCodes.All.Select(code => code.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCodeIsAScreamingSnakeIdentifier()
    {
        foreach (var code in WindowsPreflightFindingCodes.All)
        {
            Assert.Matches("^[A-Z][A-Z0-9_]*$", code.Value);
        }
    }

    [Fact]
    public void ACodesIdentityIsItsTextNotItsMessage()
    {
        // Two instances built from the same text are the same identity, whatever the finding built
        // from them says.
        Assert.Equal(new FindingCode("FAT32_FILE_TOO_LARGE"), WindowsPreflightFindingCodes.Fat32FileTooLarge);
        Assert.NotEqual(WindowsPreflightFindingCodes.Fat32FileTooLarge, WindowsPreflightFindingCodes.TargetComponentTooLong);
    }

    [Fact]
    public void EveryDeclaredCodeIsActuallyProducedByARule()
    {
        // A code that no rule can emit would be dead weight; a produced code that is not declared
        // would be an unpinned identity.
        var produced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var findings in EvaluateEveryScenario())
        {
            foreach (var finding in findings)
            {
                produced.Add(finding.Code.Value);
            }
        }

        Assert.Equal(
            ExpectedCodes.OrderBy(value => value, StringComparer.Ordinal),
            produced.OrderBy(value => value, StringComparer.Ordinal));
    }

    /// <summary>
    /// One scenario per group of rules that can fire together, chosen so that every declared code is
    /// reached at least once. Blocking and non-blocking target outcomes are mutually exclusive by
    /// design, so they cannot share a scenario.
    /// </summary>
    private static IEnumerable<IReadOnlyList<Finding>> EvaluateEveryScenario()
    {
        yield return PreflightInputs.Evaluate(
            PreflightInputs.Source(),
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathInvalid));

        yield return PreflightInputs.Evaluate(
            PreflightInputs.Source(),
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable));

        yield return PreflightInputs.Evaluate(
            PreflightInputs.Source(),
            PreflightInputs.Target(
                CapabilityValue<FileSystemName>.Unknown(),
                CapabilityValue<long>.Unknown(),
                CapabilityValue<int>.Unknown()));

        var overLongName = new string('n', 300);
        var source = PreflightInputs.SourceWithIssues(
            [
                PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked"),
                PreflightInputs.Issue(SourceScanIssueKind.CannotReadEntryMetadata, "unknown.bin"),
            ],
            SourceEntry.File(overLongName, 8L * 1024 * 1024 * 1024),
            SourceEntry.ReparsePoint("link", SourceEntryKind.File),

            // The source-only naming rules of the naming-compatibility milestone.
            SourceEntry.File("bad<.txt", 1),
            SourceEntry.File("NUL.txt", 1),
            SourceEntry.File("trail ", 1),
            SourceEntry.File("A.txt", 1),
            SourceEntry.File("a.txt", 1));

        yield return PreflightInputs.Evaluate(
            source,
            PreflightInputs.Target(
                CapabilityValue<FileSystemName>.Known(new FileSystemName("FAT32")),
                CapabilityValue<long>.Known(1024),
                CapabilityValue<int>.Known(8)));

        // The existing-object milestone: what the destinations already hold, together with the two ways
        // a check that ran can still leave part of the inventory uncovered.
        yield return PreflightInputs.Evaluate(
            PreflightInputs.Source(
                SourceEntry.File("collision.txt", 1),
                SourceEntry.File(@"dir\blocked.txt", 1),
                SourceEntry.File("reparse", 1),
                SourceEntry.ReparsePoint("source-link", SourceEntryKind.Directory),
                SourceEntry.File("denied.txt", 1),
                SourceEntry.File(@"boundary\inside.txt", 1)),
            PreflightInputs.HealthyTarget(),
            ExistingObjectInputs.ProbeResult(
                ExistingObjectObservation.Observed(
                    "collision.txt",
                    ExistingObjectInputs.Mapped("collision.txt"),
                    TargetObjectPresence.File,
                    "Scripted observation."),
                ExistingObjectObservation.Observed(
                    @"dir\blocked.txt",
                    ExistingObjectInputs.Mapped(@"dir\blocked.txt"),
                    TargetObjectPresence.Directory,
                    "Scripted observation."),
                ExistingObjectObservation.Observed(
                    "reparse",
                    ExistingObjectInputs.Mapped("reparse"),
                    TargetObjectPresence.ReparsePoint,
                    "Scripted observation."),
                ExistingObjectObservation.Observed(
                    "source-link",
                    ExistingObjectInputs.Mapped("source-link"),
                    TargetObjectPresence.File,
                    "Scripted observation."),
                ExistingObjectObservation.CheckFailed(
                    "denied.txt",
                    ExistingObjectInputs.Mapped("denied.txt"),
                    ScriptedFileSystemApi.ErrorAccessDenied,
                    "Scripted failure."),
                ExistingObjectObservation.NotQueried(
                    @"boundary\inside.txt",
                    ExistingObjectGapReason.BlockedByReparsePoint,
                    "boundary",
                    nativeErrorCode: null,
                    "Scripted boundary.",
                    ExistingObjectInputs.Mapped(@"boundary\inside.txt"))));

        // A cancelled run is a state of its own: not a completed check, and not an ordinary failure.
        yield return PreflightInputs.Evaluate(
            PreflightInputs.Source(SourceEntry.File("never-reached.txt", 1)),
            PreflightInputs.HealthyTarget(),
            ExistingObjectInputs.CancelledProbeResult(
                ExistingObjectObservation.NotQueried(
                    "never-reached.txt",
                    ExistingObjectGapReason.Cancelled,
                    blockingAncestorRelativePath: null,
                    nativeErrorCode: null,
                    "Scripted cancellation.",
                    ExistingObjectInputs.Mapped("never-reached.txt"))));
    }
}

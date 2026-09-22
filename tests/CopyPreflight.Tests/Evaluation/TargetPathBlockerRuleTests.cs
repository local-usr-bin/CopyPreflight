using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// A target that was rejected as a destination blocks the migration, and stops the target-dependent
/// rules from running.
/// </summary>
public class TargetPathBlockerRuleTests
{
    [Theory]
    [InlineData(TargetProbeIssueKind.TargetPathInvalid, "TARGET_PATH_INVALID")]
    [InlineData(TargetProbeIssueKind.TargetPathUnavailable, "TARGET_PATH_UNAVAILABLE")]
    public void ARejectedTargetBecomesATargetWideError(TargetProbeIssueKind kind, string expectedCode)
    {
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(),
                PreflightInputs.BlockedTarget(kind))
            .Only(new FindingCode(expectedCode));

        Assert.Equal(Severity.Error, finding.Severity);

        // A target-wide finding applies to no single source entry.
        Assert.Null(finding.SourceRelativePath);
    }

    [Fact]
    public void ATargetWideProblemAndASourceProblemCanBothBeReported()
    {
        // A target that cannot be used does not erase what was already learned about the source.
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked")]);

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetPathUnavailable));
    }

    [Fact]
    public void AnUnavailableTargetSuppressesEveryTargetDependentRule()
    {
        // Every condition below would produce a finding against a usable target: a FAT32 target with
        // a file beyond its limit, a tiny name limit, a destination path beyond MAX_PATH, and
        // nowhere near enough free space.
        var longName = new string('n', 300);
        var source = PreflightInputs.Source(
            SourceEntry.File(longName + ".iso", 8L * 1024 * 1024 * 1024));

        var usable = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("FAT32")),
            CapabilityValue<long>.Known(1024),
            CapabilityValue<int>.Known(8));

        var usableFindings = PreflightInputs.Evaluate(source, usable);
        Assert.Single(usableFindings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
        Assert.Single(usableFindings.WithCode(WindowsPreflightFindingCodes.TargetComponentTooLong));
        Assert.Single(usableFindings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
        Assert.Single(usableFindings.WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient));

        var blockedFindings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable));

        Assert.Single(blockedFindings.WithCode(WindowsPreflightFindingCodes.TargetPathUnavailable));
        Assert.Empty(blockedFindings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
        Assert.Empty(blockedFindings.WithCode(WindowsPreflightFindingCodes.TargetComponentTooLong));
        Assert.Empty(blockedFindings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
        Assert.Empty(blockedFindings.WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient));
    }

    [Fact]
    public void AnUnavailableTargetDoesNotAddUnknownCapabilityNoise()
    {
        // The blocking Error already says the target cannot be used; three further "this could not
        // be checked" findings would only repeat it.
        var findings = PreflightInputs.Evaluate(
            PreflightInputs.Source(),
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathInvalid));

        Assert.Single(findings);
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetFileSystemUnknown));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetComponentLimitUnknown));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetFreeSpaceUnknown));
    }

    [Fact]
    public void ReparsePointAndSourceFindingsSurviveARejectedTarget()
    {
        // These rules describe the source only, so the target's fate cannot invalidate them.
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotReadEntryMetadata, "blob.bin")],
            SourceEntry.ReparsePoint("link", SourceEntryKind.File));

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.SourceEntryMetadataFailed));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.ReparsePointPresent));
    }

    [Theory]
    [InlineData(TargetProbeIssueKind.VolumePathQueryFailed)]
    [InlineData(TargetProbeIssueKind.FreeSpaceQueryFailed)]
    [InlineData(TargetProbeIssueKind.VolumeInformationQueryFailed)]
    public void ANonBlockingProbeFailureIsNotReportedAsItsOwnFinding(TargetProbeIssueKind kind)
    {
        // These are covered once, through the capability they left unknown, instead of being shown
        // a second time as a Win32 error. Their technical detail stays in the probe result.
        var target = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Unknown(),
            CapabilityValue<long>.Unknown(),
            CapabilityValue<int>.Unknown(),
            issues:
            [
                new TargetProbeIssue(kind, PreflightInputs.TargetRoot, "SomeWin32EntryPoint", 1231, "Not observed."),
            ]);

        var findings = PreflightInputs.Evaluate(PreflightInputs.Source(), target);

        Assert.DoesNotContain(findings, finding => finding.Code == WindowsPreflightFindingCodes.TargetPathInvalid);
        Assert.DoesNotContain(findings, finding => finding.Code == WindowsPreflightFindingCodes.TargetPathUnavailable);
        Assert.DoesNotContain(
            findings,
            finding => finding.Explanation.Contains("SomeWin32EntryPoint", StringComparison.Ordinal));

        // The coverage of the gaps is still reported, exactly once per capability.
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetFileSystemUnknown));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetComponentLimitUnknown));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetFreeSpaceUnknown));
    }
}

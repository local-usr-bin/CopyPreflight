using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// What the user is told when a capability stayed unknown: coverage, once per capability, as Info.
/// </summary>
public class TargetCapabilityCoverageRuleTests
{
    [Fact]
    public void AnUnknownFileSystemNameIsReportedOnceAsInfo()
    {
        var findings = PreflightInputs
            .Evaluate(PreflightInputs.Source(), Target(filesystem: CapabilityValue<FileSystemName>.Unknown()))
            .WithCode(WindowsPreflightFindingCodes.TargetFileSystemUnknown);

        var finding = Assert.Single(findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.SourceRelativePath);
        Assert.Contains("file system specific checks", finding.Impact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownNameLimitIsReportedOnceAsInfo()
    {
        var findings = PreflightInputs
            .Evaluate(PreflightInputs.Source(), Target(componentLimit: CapabilityValue<int>.Unknown()))
            .WithCode(WindowsPreflightFindingCodes.TargetComponentLimitUnknown);

        var finding = Assert.Single(findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.SourceRelativePath);
    }

    [Fact]
    public void AnUnknownFreeSpaceIsReportedOnceAsInfo()
    {
        var findings = PreflightInputs
            .Evaluate(PreflightInputs.Source(), Target(freeBytes: CapabilityValue<long>.Unknown()))
            .WithCode(WindowsPreflightFindingCodes.TargetFreeSpaceUnknown);

        var finding = Assert.Single(findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.SourceRelativePath);
    }

    [Fact]
    public void EachUnknownCapabilityIsReportedOnceRegardlessOfHowManyEntriesWereObserved()
    {
        // A gap is a property of the target, so it is stated once instead of once per source entry.
        var entries = Enumerable.Range(0, 200)
            .Select(index => SourceEntry.File($"file{index}.txt", 1))
            .ToArray();

        var source = PreflightInputs.Source(entries);

        var findings = PreflightInputs.Evaluate(
            source,
            Target(
                filesystem: CapabilityValue<FileSystemName>.Unknown(),
                freeBytes: CapabilityValue<long>.Unknown(),
                componentLimit: CapabilityValue<int>.Unknown()),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetFileSystemUnknown));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetComponentLimitUnknown));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetFreeSpaceUnknown));
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void AFullyDescribedTargetProducesNoCoverageFindings()
    {
        var source = PreflightInputs.Source();

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Empty(findings);
    }

    [Fact]
    public void AnUnknownCapabilityIsNeverTurnedIntoAnError()
    {
        var findings = PreflightInputs.Evaluate(
            PreflightInputs.Source(),
            Target(
                filesystem: CapabilityValue<FileSystemName>.Unknown(),
                freeBytes: CapabilityValue<long>.Unknown(),
                componentLimit: CapabilityValue<int>.Unknown()));

        Assert.All(findings, finding => Assert.Equal(Severity.Info, finding.Severity));
    }

    [Fact]
    public void OnlyTheUnknownCapabilitiesAreCovered()
    {
        // A target whose free space is known but whose file system is not: exactly one gap to report,
        // and the known capability must not produce a finding of its own.
        var source = PreflightInputs.Source();

        var findings = PreflightInputs.Evaluate(
            source,
            Target(filesystem: CapabilityValue<FileSystemName>.Unknown(), freeBytes: CapabilityValue<long>.Known(1)),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Single(findings);
        Assert.Equal(WindowsPreflightFindingCodes.TargetFileSystemUnknown, findings[0].Code);
    }

    private static TargetProbeResult Target(
        CapabilityValue<FileSystemName>? filesystem = null,
        CapabilityValue<long>? freeBytes = null,
        CapabilityValue<int>? componentLimit = null)
        => PreflightInputs.Target(
            filesystem ?? CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            freeBytes ?? CapabilityValue<long>.Known(long.MaxValue),
            componentLimit ?? CapabilityValue<int>.Known(255));
}

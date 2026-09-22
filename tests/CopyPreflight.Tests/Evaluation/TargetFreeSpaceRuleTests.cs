using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The conservative space comparison, which warns and never concludes.
/// </summary>
public class TargetFreeSpaceRuleTests
{
    private const long AvailableFreeBytes = 1_000_000;

    [Theory]
    [InlineData(AvailableFreeBytes + 1, true)]
    [InlineData(AvailableFreeBytes * 2, true)]
    [InlineData(AvailableFreeBytes, false)]
    [InlineData(AvailableFreeBytes - 1, false)]
    [InlineData(0, false)]
    public void TheComparisonIsStrictlyLargerThanTheReportedFreeSpace(long sourceBytes, bool expectWarning)
    {
        var source = PreflightInputs.Source(SourceEntry.File("data.bin", sourceBytes));

        var findings = PreflightInputs.Evaluate(source, TargetWithFreeSpace(AvailableFreeBytes))
            .WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient);

        if (expectWarning)
        {
            var finding = Assert.Single(findings);

            // Without collision probing and copy policies, an Error would claim more certainty than
            // the observed facts support.
            Assert.Equal(Severity.Warning, finding.Severity);

            // The amount of space is a property of the target, not of one source entry.
            Assert.Null(finding.SourceRelativePath);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void AnUnknownFreeSpaceMeansNoComparisonIsMade()
    {
        var source = PreflightInputs.Source(SourceEntry.File("data.bin", 64L * 1024 * 1024 * 1024));
        var target = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Unknown(),
            CapabilityValue<int>.Known(255));

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient));
    }

    [Fact]
    public void ReparsePointsDoNotCountTowardsTheObservedSize()
    {
        // No length was attributed to a reparse point, so it adds nothing — not even a guessed zero
        // that would be indistinguishable from an empty file.
        var source = PreflightInputs.Source(
            SourceEntry.ReparsePoint(@"links\huge", SourceEntryKind.File),
            SourceEntry.ReparsePoint(@"links\dir", SourceEntryKind.Directory));

        // Guard: nothing was attributed to them, so there is nothing for the comparison to use.
        Assert.Equal(0, source.LogicalFileBytes);

        var findings = PreflightInputs.Evaluate(source, TargetWithFreeSpace(0));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient));
    }

    [Fact]
    public void EnoughSpaceProducesNoReassuringFindingEither()
    {
        // Fitting inside the reported free space does not prove the copy will fit, so nothing is
        // reported in that case. Only observed problems are reported.
        var source = PreflightInputs.Source(SourceEntry.File("small.txt", 10));

        var findings = PreflightInputs.Evaluate(
            source,
            TargetWithFreeSpace(AvailableFreeBytes),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Empty(findings);
    }

    [Fact]
    public void TheFindingExplainsWhyItIsOnlyAWarning()
    {
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.File("data.bin", AvailableFreeBytes + 1)),
                TargetWithFreeSpace(AvailableFreeBytes))
            .Only(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient);

        var text = $"{finding.Explanation} {finding.Impact} {finding.Recommendation}";

        Assert.Contains("not a certainty", finding.Impact, StringComparison.Ordinal);
        Assert.DoesNotContain("will fail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insufficient space", text, StringComparison.OrdinalIgnoreCase);

        // The comparison is about logical sizes, and the finding says so rather than implying it
        // knows what the copy will really need.
        Assert.Contains("logical", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static TargetProbeResult TargetWithFreeSpace(long availableFreeBytes)
        => PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Known(availableFreeBytes),
            CapabilityValue<int>.Known(255));
}

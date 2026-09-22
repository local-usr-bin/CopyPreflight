using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The target's maximum name length, measured on each entry's own leaf component and nothing else.
/// </summary>
public class TargetPathComponentRuleTests
{
    private const int TargetMaxNameLength = 10;

    [Theory]
    [InlineData(TargetMaxNameLength, false)]
    [InlineData(TargetMaxNameLength - 1, false)]
    [InlineData(TargetMaxNameLength + 1, true)]
    [InlineData(TargetMaxNameLength + 50, true)]
    public void ANameOfExactlyTheLimitIsLegal(int nameLength, bool expectedFinding)
    {
        var name = new string('d', nameLength);
        var source = PreflightInputs.Source(SourceEntry.Directory(name));

        var findings = PreflightInputs.Evaluate(source, TargetWithNameLimit()).WithCode(
            WindowsPreflightFindingCodes.TargetComponentTooLong);

        if (expectedFinding)
        {
            var finding = Assert.Single(findings);

            Assert.Equal(Severity.Error, finding.Severity);
            Assert.Equal(name, finding.SourceRelativePath);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void TheRuleAppliesToFilesAndDirectoriesAlike()
    {
        var tooLong = new string('n', TargetMaxNameLength + 1);
        var source = PreflightInputs.Source(
            SourceEntry.Directory(tooLong),
            SourceEntry.File(tooLong + ".txt", 10));

        var findings = PreflightInputs.Evaluate(source, TargetWithNameLimit()).WithCode(
            WindowsPreflightFindingCodes.TargetComponentTooLong);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.SourceRelativePath == tooLong);
        Assert.Contains(findings, finding => finding.SourceRelativePath == tooLong + ".txt");
    }

    [Fact]
    public void OnlyTheEntrysOwnNameIsMeasured_NotThePathLeadingToIt()
    {
        // The over-long directory has its own entry and is reported there. Its descendants keep their
        // own names, which are short, so one bad directory name cannot produce an Error per descendant.
        var overLongDirectory = new string('d', TargetMaxNameLength + 1);
        var nested = $@"{overLongDirectory}\sub";

        var source = PreflightInputs.Source(
            SourceEntry.Directory(overLongDirectory),
            SourceEntry.Directory(nested),
            SourceEntry.File($@"{nested}\a.txt", 1),
            SourceEntry.File($@"{nested}\b.txt", 1),
            SourceEntry.File($@"{nested}\legalname1", 1));

        var findings = PreflightInputs.Evaluate(source, TargetWithNameLimit()).WithCode(
            WindowsPreflightFindingCodes.TargetComponentTooLong);

        Assert.Single(findings);
        Assert.Equal(overLongDirectory, findings[0].SourceRelativePath);
    }

    [Fact]
    public void AnUnknownLimitSkipsTheRuleInsteadOfAssumingADefault()
    {
        // Different file systems accept different name lengths, and different code pages change it
        // again, so there is no safe default. 255 in particular must not be assumed.
        var source = PreflightInputs.Source(SourceEntry.Directory(new string('d', 300)));

        var target = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Known(long.MaxValue),
            CapabilityValue<int>.Unknown());

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetComponentTooLong));
    }

    [Fact]
    public void TheFindingShowsBothLengthsSoTheRationaleIsVisible()
    {
        var name = new string('n', TargetMaxNameLength + 1);
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.File(name, 10)),
                TargetWithNameLimit())
            .Only(WindowsPreflightFindingCodes.TargetComponentTooLong);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains(name, finding.Explanation, StringComparison.Ordinal);
        Assert.Contains(name.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), finding.Explanation, StringComparison.Ordinal);
        Assert.Contains(TargetMaxNameLength.ToString(System.Globalization.CultureInfo.InvariantCulture), finding.Explanation, StringComparison.Ordinal);

        // CopyPreflight never renames anything, and the advice must not imply otherwise.
        Assert.Contains("never renames", finding.Recommendation, StringComparison.Ordinal);
    }

    private static TargetProbeResult TargetWithNameLimit()
        => PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Known(long.MaxValue),
            CapabilityValue<int>.Known(TargetMaxNameLength));
}

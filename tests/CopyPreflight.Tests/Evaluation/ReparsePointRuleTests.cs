using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The reparse point warning: generic by design, because the kind is not classified yet.
/// </summary>
public class ReparsePointRuleTests
{
    [Theory]
    [InlineData(SourceEntryKind.File)]
    [InlineData(SourceEntryKind.Directory)]
    public void EveryReparsePointIsReportedWithAWarning(SourceEntryKind shape)
    {
        var source = PreflightInputs.Source(SourceEntry.ReparsePoint(@"links\thing", shape));

        var finding = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.ReparsePointPresent);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(@"links\thing", finding.SourceRelativePath);
    }

    [Fact]
    public void OneEntryProducesExactlyOneFinding()
    {
        var source = PreflightInputs.Source(
            SourceEntry.ReparsePoint("a", SourceEntryKind.File),
            SourceEntry.ReparsePoint("b", SourceEntryKind.Directory),
            SourceEntry.File("c.txt", 1));

        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget());

        Assert.Equal(2, findings.WithCode(WindowsPreflightFindingCodes.ReparsePointPresent).Count);
    }

    [Fact]
    public void TheFindingNeverNamesASpecificKindOfReparsePoint()
    {
        // The scanner recorded the attribute and nothing else, so naming a kind would state a fact
        // that was never observed.
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.ReparsePoint("link", SourceEntryKind.File)),
                PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.ReparsePointPresent);

        foreach (var specificKind in new[] { "symlink", "symbolic link", "junction", "mount point" })
        {
            Assert.DoesNotContain(specificKind, finding.Title, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("reparse point", finding.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not classify", finding.Explanation, StringComparison.Ordinal);
        Assert.Contains("does not read", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWarningLeavesTheOutcomeToTheCopyProgram()
    {
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.ReparsePoint("link", SourceEntryKind.File)),
                PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.ReparsePointPresent);

        Assert.Contains("differently", finding.Impact, StringComparison.Ordinal);
        Assert.Contains("depends on the program", finding.Impact, StringComparison.Ordinal);
        Assert.Contains("copy program", finding.Recommendation, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryEntriesAreNotReported()
    {
        var source = PreflightInputs.Source(
            SourceEntry.File("file.txt", 1),
            SourceEntry.Directory("folder"));

        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget());

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.ReparsePointPresent));
    }
}

using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The case-only collision warning: which names are grouped together, and why the verdict stays a
/// Warning.
/// </summary>
public class SourceCaseCollisionRuleTests
{
    [Fact]
    public void TwoNamesDifferingOnlyByCaseProduceOneWarning()
    {
        var finding = Evaluate(
                SourceEntry.File("A.txt", 1),
                SourceEntry.File("a.txt", 2))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("'A.txt'", finding.Explanation, StringComparison.Ordinal);
        Assert.Contains("'a.txt'", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AThreeWayCollisionIsStillASingleFinding()
    {
        // One collision group is one finding. Reporting it pairwise would turn a single real problem
        // into three findings.
        var finding = Evaluate(
                SourceEntry.File("A.txt", 1),
                SourceEntry.File("A.TXT", 2),
                SourceEntry.File("a.txt", 3))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        // The names are listed in Ordinal order, so the text is fixed by which names were observed
        // rather than by the order the inventory arrived in.
        Assert.Contains("'A.TXT', 'A.txt', 'a.txt'", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesInDifferentDirectoriesDoNotCollide()
    {
        var findings = Evaluate(
            SourceEntry.File(@"dir1\A.txt", 1),
            SourceEntry.File(@"dir2\a.txt", 2));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk));
    }

    [Fact]
    public void TheFindingIsScopedToTheDirectoryTheNamesSitIn()
    {
        var finding = Evaluate(
                SourceEntry.Directory("dir"),
                SourceEntry.File(@"dir\A.txt", 1),
                SourceEntry.File(@"dir\a.txt", 2))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Equal("dir", finding.SourceRelativePath);
        Assert.Contains("'A.txt', 'a.txt'", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultipleCollisionGroupIsStillOneFinding()
    {
        // Two independent groups in one tree: one at the root and one under "sub".
        var findings = Evaluate(
            SourceEntry.File("A.txt", 1),
            SourceEntry.File("a.txt", 2),
            SourceEntry.File(@"sub\B.txt", 3),
            SourceEntry.File(@"sub\b.txt", 4));

        var collisions = findings.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Equal(2, collisions.Count);
        Assert.Contains(collisions, finding => finding.SourceRelativePath is null);
        Assert.Contains(collisions, finding => finding.SourceRelativePath == "sub");
    }

    [Fact]
    public void TheParentDirectoryIsPartOfTheKeyAndIsMatchedExactly()
    {
        // "Dir" and "dir" are two different relative directories in the inventory, so the names
        // inside them belong to different groups. That the two directories may themselves collide is
        // reported separately, for those two directory names.
        var finding = Evaluate(
                SourceEntry.Directory("Dir"),
                SourceEntry.Directory("dir"),
                SourceEntry.File(@"Dir\A.txt", 1),
                SourceEntry.File(@"dir\a.txt", 2))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Null(finding.SourceRelativePath);
        Assert.Contains("'Dir', 'dir'", finding.Explanation, StringComparison.Ordinal);

        // The two file names are not dragged into it: they do not sit in the same relative directory.
        Assert.DoesNotContain("A.txt", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryDirectoryProducesNothing()
    {
        var findings = Evaluate(
            SourceEntry.File(@"docs\report.docx", 1024),
            SourceEntry.Directory("media"),
            SourceEntry.File(@"media\clip.mp4", 2048));

        Assert.Empty(findings);
    }

    [Fact]
    public void ASingleEntryIsNotACollision()
    {
        Assert.Empty(Evaluate(SourceEntry.File("A.txt", 1))
            .WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk));
    }

    [Fact]
    public void IdenticallyNamedEntriesAreNotACaseCollision()
    {
        // The same name twice is one name, not two names that differ only in case.
        Assert.Empty(Evaluate(
                SourceEntry.File("A.txt", 1),
                SourceEntry.File("A.txt", 2))
            .WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk));
    }

    [Fact]
    public void FilesDirectoriesAndReparsePointsAllTakePartInTheGrouping()
    {
        // Every entry occupies a name, whatever it is, so all three kinds are grouped together. The
        // reparse point is neither followed nor classified to reach this conclusion.
        var finding = Evaluate(
                SourceEntry.Directory("Folder"),
                SourceEntry.File("folder", 1),
                SourceEntry.ReparsePoint("FOLDER", SourceEntryKind.File))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Contains("'FOLDER', 'Folder', 'folder'", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVerdictIsAlwaysAWarningAndNeverAnError()
    {
        // Typical Windows file systems are case-insensitive, which is why this is worth reporting;
        // but NTFS supports per-directory case sensitivity, and CopyPreflight has probed nothing, so
        // a definite conflict cannot be claimed.
        var finding = Evaluate(
                SourceEntry.File("A.txt", 1),
                SourceEntry.File("a.txt", 2))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void TheWarningSaysTheNamesMayCollideRatherThanThatTheyWill()
    {
        var finding = Evaluate(
                SourceEntry.File("A.txt", 1),
                SourceEntry.File("a.txt", 2))
            .Only(WindowsPreflightFindingCodes.SourceCaseCollisionRisk);

        var text = $"{finding.Explanation} {finding.Impact} {finding.Recommendation}";

        Assert.DoesNotContain("will fail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will collide", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certainly", text, StringComparison.OrdinalIgnoreCase);

        // The reader is told the outcome depends on the copy program and the target, that a
        // case-sensitive target is possible, and that this was not probed.
        Assert.Contains("may", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("case-sensitive", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("depends", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not probed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReversingTheInventoryDoesNotChangeTheReport()
    {
        var forward = Evaluate(
            SourceEntry.File("A.txt", 1),
            SourceEntry.File(@"sub\B.txt", 2),
            SourceEntry.File("a.txt", 3),
            SourceEntry.File(@"sub\b.txt", 4));

        var backward = Evaluate(
            SourceEntry.File(@"sub\b.txt", 4),
            SourceEntry.File("a.txt", 3),
            SourceEntry.File(@"sub\B.txt", 2),
            SourceEntry.File("A.txt", 1));

        Assert.Equal(forward.Shape(), backward.Shape());
        Assert.Equal(
            forward.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk).Select(f => f.Explanation),
            backward.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk).Select(f => f.Explanation));
    }

    [Fact]
    public void TheCollisionRuleDoesNotDependOnTheTargetBeingUsable()
    {
        var source = PreflightInputs.Source(
            SourceEntry.File("A.txt", 1),
            SourceEntry.File("a.txt", 2));

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathInvalid));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk));
    }

    [Fact]
    public void TheCollisionRuleReadsNothingFromTheTargetDirectory()
    {
        var missingTargetPath = Path.Combine(
            Path.GetTempPath(),
            "copy-preflight-case-" + Guid.NewGuid().ToString("N"));

        Assert.False(Directory.Exists(missingTargetPath));

        var source = PreflightInputs.Source(
            SourceEntry.File("A.txt", 1),
            SourceEntry.File("a.txt", 2));

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(missingTargetPath));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.SourceCaseCollisionRisk));
    }

    private static IReadOnlyList<Finding> Evaluate(params SourceEntry[] entries)
    {
        // The case-collision check is source-only, so the destination check is stated to have found
        // nothing: that keeps the absence of collision findings a fact rather than a missing check.
        var source = PreflightInputs.Source(entries);

        return PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));
    }
}

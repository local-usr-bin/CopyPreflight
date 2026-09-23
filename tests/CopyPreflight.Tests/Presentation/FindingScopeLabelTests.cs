using CopyPreflight.App;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// What the list says a finding belongs to when it belongs to no single source entry.
/// </summary>
/// <remarks>
/// A finding about the source root and a finding about the target both carry no relative path, and
/// telling the reader the wrong one points them at the wrong half of the check. The label is decided by
/// the rule behind the finding, so these tests state each case explicitly.
/// </remarks>
public class FindingScopeLabelTests
{
    private const string TargetWideLabel = "Target-wide (not tied to a single source object)";

    private const string SourceWideLabel = "Source-wide (not tied to a single source entry)";

    [Fact]
    public void ASourceRootFindingIsNotLabelledAsATargetProblem()
    {
        var row = Row(new Finding(
            WindowsPreflightFindingCodes.SourceRootUnusable,
            Severity.Error,
            "Source directory does not exist",
            "Nothing was observed.",
            "Nothing could be concluded.",
            "Check the source directory."));

        Assert.Equal(SourceWideLabel, row.SourcePathText);
        Assert.NotEqual(TargetWideLabel, row.SourcePathText);
        Assert.Null(row.SourceRelativePath);
    }

    [Fact]
    public void ASourceRootThatCouldNotBeListedIsNotLabelledAsATargetProblem()
    {
        // The same pathless source fact reaches the list through the enumeration rule, and it belongs
        // to the source root just as much.
        var row = Row(new Finding(
            WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed,
            Severity.Error,
            "Source directory could not be fully listed",
            "Nothing was observed.",
            "Nothing could be concluded.",
            "Check the directory."));

        Assert.Equal(SourceWideLabel, row.SourcePathText);
    }

    [Fact]
    public void ATargetWideFindingStaysTargetWide()
    {
        var row = Row(new Finding(
            WindowsPreflightFindingCodes.TargetPathUnavailable,
            Severity.Error,
            "Target directory is not available",
            "No usable directory could be reached.",
            "Nothing can be migrated.",
            "Check the target directory."));

        Assert.Equal(TargetWideLabel, row.SourcePathText);
    }

    [Fact]
    public void AnEntryLevelFindingKeepsItsOwnPath()
    {
        var row = Row(new Finding(
            WindowsPreflightFindingCodes.TargetExistingFileCollision,
            Severity.Warning,
            "A file already occupies this destination",
            "The destination holds a file.",
            "The copy would have to decide what to do.",
            "Decide before copying.",
            @"nested\dup.txt"));

        Assert.Equal(@"nested\dup.txt", row.SourcePathText);
    }

    private static FindingRow Row(Finding finding)
        => FindingRow.FromFindings([finding])[0];
}

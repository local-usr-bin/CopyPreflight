using System.IO;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The scan result: counters that are derived from the inventory, so the summary can never disagree
/// with what it summarises.
/// </summary>
public class SourceScanResultTests
{
    private static readonly SourceEntry[] MixedEntries =
    [
        SourceEntry.File("a.txt", 10L),
        SourceEntry.Directory("sub"),
        SourceEntry.ReparsePoint("link", SourceEntryKind.Directory),
        SourceEntry.ReparsePoint("placeholder.bin", SourceEntryKind.File),
        SourceEntry.File(Path.Combine("sub", "b.bin"), 5L),
    ];

    [Fact]
    public void CountersAreDerivedFromTheObservedEntries()
    {
        var result = new SourceScanResult(@"C:\Data", MixedEntries, []);

        Assert.Equal(5, result.Entries.Count);
        Assert.Equal(2L, result.FileCount);
        Assert.Equal(1L, result.DirectoryCount);
        Assert.Equal(2L, result.ReparsePointCount);
        Assert.Equal(15L, result.LogicalFileBytes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void CountersAreDisjointAndExhaustive()
    {
        var result = new SourceScanResult(@"C:\Data", MixedEntries, []);

        Assert.Equal(
            (long)result.Entries.Count,
            result.FileCount + result.DirectoryCount + result.ReparsePointCount);

        Assert.Equal(
            result.Entries.Where(entry => entry.LengthInBytes is not null).Sum(entry => entry.LengthInBytes!.Value),
            result.LogicalFileBytes);
    }

    [Fact]
    public void ReparsePointsContributeNoBytesEvenWhenShapedLikeFiles()
    {
        var result = new SourceScanResult(
            @"C:\Data",
            [SourceEntry.ReparsePoint("placeholder.bin", SourceEntryKind.File)],
            []);

        Assert.Equal(0L, result.FileCount);
        Assert.Equal(1L, result.ReparsePointCount);
        Assert.Equal(0L, result.LogicalFileBytes);
    }

    [Fact]
    public void AnEmptyTreeIsAValidCompleteResult()
    {
        var result = new SourceScanResult(@"C:\Data", [], []);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Issues);
        Assert.Equal(0L, result.FileCount);
        Assert.Equal(0L, result.DirectoryCount);
        Assert.Equal(0L, result.ReparsePointCount);
        Assert.Equal(0L, result.LogicalFileBytes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void ScanIssuesAreKeptSeparateFromFindingsAndMakeTheScanIncomplete()
    {
        var issues = new[]
        {
            SourceScanIssue.FromException(
                SourceScanIssueKind.CannotEnumerateDirectory,
                "sub",
                new UnauthorizedAccessException("Access to the path is denied.")),
        };

        var result = new SourceScanResult(@"C:\Data", MixedEntries, issues);

        Assert.False(result.IsComplete);
        Assert.Equal(MixedEntries, result.Entries);
        Assert.Equal(issues, result.Issues);
    }

    [Fact]
    public void KeepsTheResolvedSourceRootAndTheObservationOrder()
    {
        var result = new SourceScanResult(Path.GetFullPath(@"C:\Data"), MixedEntries, []);

        Assert.Equal(Path.GetFullPath(@"C:\Data"), result.SourceRoot);
        Assert.Equal(MixedEntries, result.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsAMissingSourceRoot(string? sourceRoot)
    {
        Assert.Throws<ArgumentException>(() => new SourceScanResult(sourceRoot!, MixedEntries, []));
    }

    [Fact]
    public void RejectsMissingCollections()
    {
        Assert.Throws<ArgumentNullException>(() => new SourceScanResult(@"C:\Data", null!, []));
        Assert.Throws<ArgumentNullException>(() => new SourceScanResult(@"C:\Data", MixedEntries, null!));
    }
}

using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The progress snapshot: counters that cannot be negative and carry no fabricated percentage.
/// </summary>
public class SourceScanProgressTests
{
    [Fact]
    public void CarriesTheObservedCountersAndTheCurrentLocation()
    {
        var progress = new SourceScanProgress(
            entriesObserved: 12,
            filesObserved: 9,
            directoriesObserved: 3,
            reparsePointsObserved: 0,
            logicalBytesObserved: 4_096,
            issuesObserved: 1,
            currentRelativePath: @"sub\inner");

        Assert.Equal(12L, progress.EntriesObserved);
        Assert.Equal(9L, progress.FilesObserved);
        Assert.Equal(3L, progress.DirectoriesObserved);
        Assert.Equal(0L, progress.ReparsePointsObserved);
        Assert.Equal(4_096L, progress.LogicalBytesObserved);
        Assert.Equal(1L, progress.IssuesObserved);
        Assert.Equal(@"sub\inner", progress.CurrentRelativePath);
    }

    [Fact]
    public void AllowsNoCurrentLocationWhenWorkingAtTheSourceRoot()
    {
        var progress = new SourceScanProgress(0, 0, 0, 0, 0, 0, currentRelativePath: null);

        Assert.Null(progress.CurrentRelativePath);
    }

    [Theory]
    [InlineData(-1L, 0L, 0L, 0L, 0L, 0L)]
    [InlineData(0L, -1L, 0L, 0L, 0L, 0L)]
    [InlineData(0L, 0L, -1L, 0L, 0L, 0L)]
    [InlineData(0L, 0L, 0L, -1L, 0L, 0L)]
    [InlineData(0L, 0L, 0L, 0L, -1L, 0L)]
    [InlineData(0L, 0L, 0L, 0L, 0L, -1L)]
    public void RejectsNegativeCounters(
        long entriesObserved,
        long filesObserved,
        long directoriesObserved,
        long reparsePointsObserved,
        long logicalBytesObserved,
        long issuesObserved)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceScanProgress(
            entriesObserved,
            filesObserved,
            directoriesObserved,
            reparsePointsObserved,
            logicalBytesObserved,
            issuesObserved,
            currentRelativePath: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsABlankCurrentLocation(string currentRelativePath)
    {
        Assert.Throws<ArgumentException>(() => new SourceScanProgress(
            0, 0, 0, 0, 0, 0, currentRelativePath));
    }
}

using System.IO;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The source scanner: what it observes in a real directory tree, and how it behaves when it cannot
/// observe everything.
/// </summary>
public class SourceScannerTests
{
    // ---------------------------------------------------------------- inventory

    [Fact]
    public void Scan_OfAnEmptyRoot_ObservesNothing()
    {
        using var tree = new TemporarySourceTree();

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Issues);
        Assert.Equal(0L, result.FileCount);
        Assert.Equal(0L, result.DirectoryCount);
        Assert.Equal(0L, result.ReparsePointCount);
        Assert.Equal(0L, result.LogicalFileBytes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Scan_OfOneLevel_ObservesFilesAndEmptyDirectories()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("a.txt", 5);
        tree.CreateFile("empty.txt", 0);
        tree.CreateDirectory("empty-directory");

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Equal(2L, result.FileCount);
        Assert.Equal(1L, result.DirectoryCount);
        Assert.Equal(5L, result.LogicalFileBytes);
        Assert.Contains(result.Entries, entry => entry.RelativePath == "a.txt");
        Assert.Contains(result.Entries, entry => entry.RelativePath == "empty.txt");
        Assert.Contains(result.Entries, entry => entry.RelativePath == "empty-directory");
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Scan_OfAZeroLengthFile_ReportsAZeroLength()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("empty.txt", 0);

        var result = new SourceScanner().Scan(tree.Root);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(SourceEntryKind.File, entry.Kind);
        Assert.Equal(0L, entry.LengthInBytes);
        Assert.Equal(0L, result.LogicalFileBytes);
    }

    [Fact]
    public void Scan_OfANestedTree_ObservesEveryEntryWithARelativePath()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("root.txt", 1);
        tree.CreateFile(Path.Combine("sub", "b.bin"), 2);
        tree.CreateFile(Path.Combine("sub", "deeper", "c.dat"), 3);
        tree.CreateDirectory(Path.Combine("sub", "empty"));

        var result = new SourceScanner().Scan(tree.Root);

        var expected = new[]
        {
            "root.txt",
            "sub",
            Path.Combine("sub", "b.bin"),
            Path.Combine("sub", "deeper"),
            Path.Combine("sub", "deeper", "c.dat"),
            Path.Combine("sub", "empty"),
        };

        // Inventories are compared as sets: enumeration order is an artifact and means nothing.
        Assert.Equal(Canonical(expected), Canonical(result.Entries.Select(entry => entry.RelativePath)));
        Assert.Equal(3L, result.FileCount);
        Assert.Equal(3L, result.DirectoryCount);
        Assert.Equal(6L, result.LogicalFileBytes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Scan_NeverLeaksTheAbsoluteSourceRootInEntryPaths()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile(Path.Combine("sub", "b.bin"), 2);

        var result = new SourceScanner().Scan(tree.Root);

        Assert.NotEmpty(result.Entries);
        Assert.Equal(Path.GetFullPath(tree.Root), result.SourceRoot);

        foreach (var entry in result.Entries)
        {
            Assert.False(Path.IsPathRooted(entry.RelativePath), $"'{entry.RelativePath}' must be relative.");
            Assert.DoesNotContain(tree.Root, entry.RelativePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Scan_NeverCreatesAnEntryForTheSourceRootItself()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("a.txt", 1);

        var result = new SourceScanner().Scan(tree.Root);

        foreach (var entry in result.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.RelativePath));
            Assert.NotEqual(tree.Root, entry.RelativePath);
            Assert.NotEqual(Path.GetFullPath(tree.Root), entry.RelativePath);
        }
    }

    [Fact]
    public void Scan_CountsDirectoriesWithoutCountingTheRoot()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile(Path.Combine("one", "two", "three", "leaf.txt"), 1);

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Equal(1L, result.FileCount);
        Assert.Equal(3L, result.DirectoryCount);
        Assert.Equal(result.Entries.Count, result.FileCount + result.DirectoryCount + result.ReparsePointCount);
    }

    [Fact]
    public void Scan_AttributesFileLengthsAndTheirTotal()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("small.bin", 7);
        tree.CreateFile(Path.Combine("sub", "large.bin"), 1_234);
        tree.CreateFile(Path.Combine("sub", "empty.bin"), 0);
        tree.CreateDirectory("no-length-here");

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Equal(7L, Assert.Single(result.Entries, entry => entry.RelativePath == "small.bin").LengthInBytes);
        Assert.Equal(1_234L, Assert.Single(result.Entries, entry => entry.RelativePath == Path.Combine("sub", "large.bin")).LengthInBytes);
        Assert.Equal(0L, Assert.Single(result.Entries, entry => entry.RelativePath == Path.Combine("sub", "empty.bin")).LengthInBytes);
        Assert.Null(Assert.Single(result.Entries, entry => entry.RelativePath == "no-length-here").LengthInBytes);

        Assert.Equal(1_241L, result.LogicalFileBytes);
    }

    [Fact]
    public void Scan_DoesNotDependOnEnumerationOrder()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("z.txt", 1);
        tree.CreateFile("a.txt", 2);
        tree.CreateFile(Path.Combine("m", "b.txt"), 3);

        var scanner = new SourceScanner();
        var first = scanner.Scan(tree.Root);
        var second = scanner.Scan(tree.Root);

        Assert.Equal(
            Canonical(first.Entries.Select(entry => entry.RelativePath)),
            Canonical(second.Entries.Select(entry => entry.RelativePath)));

        Assert.Equal(3L, first.FileCount);
        Assert.Equal(1L, first.DirectoryCount);
        Assert.Equal(6L, first.LogicalFileBytes);
        Assert.True(first.IsComplete);
    }

    // ---------------------------------------------------------------- source root

    [Fact]
    public void Scan_OfAMissingSourceRoot_FailsPredictably()
    {
        using var tree = new TemporarySourceTree();
        var missing = tree.PathOf("does-not-exist");

        var exception = Assert.Throws<InvalidSourceRootException>(() => new SourceScanner().Scan(missing));

        Assert.Equal(InvalidSourceRootReason.NotFound, exception.Reason);
        Assert.Equal(missing, exception.SourceRoot);

        // The rejection is decided on the resolved path, so a caller can report the location that was
        // actually refused without resolving it a second time.
        Assert.Equal(Path.GetFullPath(missing), exception.ResolvedSourceRoot);
    }

    [Fact]
    public void Scan_OfAFileAsSourceRoot_FailsPredictably()
    {
        using var tree = new TemporarySourceTree();
        var file = tree.CreateFile("a-file.txt", 3);

        var exception = Assert.Throws<InvalidSourceRootException>(() => new SourceScanner().Scan(file));

        Assert.Equal(InvalidSourceRootReason.NotADirectory, exception.Reason);
        Assert.Equal(file, exception.SourceRoot);
        Assert.Equal(Path.GetFullPath(file), exception.ResolvedSourceRoot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Scan_RejectsAMissingSourceRootPath(string? sourceRoot)
    {
        Assert.Throws<ArgumentException>(() => new SourceScanner().Scan(sourceRoot!));
    }

    // ---------------------------------------------------------------- cancellation

    [Fact]
    public void Scan_WithAPreCancelledToken_ThrowsBeforeObservingAnything()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("a.txt", 1);

        var reader = new ScriptedDirectoryReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => new SourceScanner(reader).Scan(tree.Root, progress: null, cancellation.Token));

        // Cancellation is checked before the walk starts, and is not folded into a partial result.
        Assert.Empty(reader.ListedDirectories);
    }

    [Fact]
    public void Scan_WhenCancelledDuringTheWalk_StopsBeforeFinishing()
    {
        using var tree = new TemporarySourceTree();
        for (var index = 0; index < 64; index++)
        {
            tree.CreateFile(Path.Combine($"dir{index:D2}", "file.txt"), 4);
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(snapshot =>
        {
            if (snapshot.EntriesObserved > 0)
            {
                cancellation.Cancel();
            }
        });

        Assert.Throws<OperationCanceledException>(
            () => new SourceScanner().Scan(tree.Root, progress, cancellation.Token));

        // The root is listed and reported once; the remaining 63 directories are then abandoned
        // instead of being walked and the cancellation noticed at the end.
        Assert.Single(progress.Snapshots);
    }

    [Fact]
    public void Scan_WhenCancelledInsideOneLargeDirectory_StopsPullingEntries()
    {
        const int CancelAfterChildren = 8;
        const int MaximumChildren = 100_000;

        using var tree = new TemporarySourceTree();
        using var cancellation = new CancellationTokenSource();

        var pulled = 0;
        var reader = new ScriptedDirectoryReader()
            .WithListing(tree.Root, EnumerateChildren);

        IEnumerable<ObservedDirectoryChild> EnumerateChildren()
        {
            for (var index = 0; index < MaximumChildren; index++)
            {
                pulled++;
                if (pulled == CancelAfterChildren)
                {
                    cancellation.Cancel();
                }

                yield return Observations.FileItem($"file{index:D6}.bin", 1L);
            }
        }

        Assert.Throws<OperationCanceledException>(
            () => new SourceScanner(reader).Scan(tree.Root, progress: null, cancellation.Token));

        // A single directory holding a great many entries is still interruptible: the walk stopped
        // as soon as the token was cancelled rather than draining all of them.
        Assert.InRange(pulled, CancelAfterChildren, CancelAfterChildren + 1);
    }

    // ---------------------------------------------------------------- recoverable failures

    [Fact]
    public void Scan_WhenADirectoryCannotBeListed_RecordsAnIssueAndKeepsWalking()
    {
        using var tree = new TemporarySourceTree();
        var broken = tree.PathOf("broken");
        var healthy = tree.PathOf("healthy");

        var reader = new ScriptedDirectoryReader()
            .WithChildren(
                tree.Root,
                Observations.DirectoryItem("broken"),
                Observations.DirectoryItem("healthy"),
                Observations.FileItem("top.txt", 3L))
            .WithFailure(broken, new UnauthorizedAccessException("Access to the path is denied."))
            .WithChildren(healthy, Observations.FileItem("inside.txt", 5L));

        var result = new SourceScanner(reader).Scan(tree.Root);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(SourceScanIssueKind.CannotEnumerateDirectory, issue.Kind);
        Assert.Equal("broken", issue.RelativePath);
        Assert.Equal("System.UnauthorizedAccessException", issue.ExceptionTypeName);
        Assert.False(result.IsComplete);

        // Everything else in the tree was still observed.
        Assert.Contains(result.Entries, entry => entry.RelativePath == "healthy");
        Assert.Contains(result.Entries, entry => entry.RelativePath == Path.Combine("healthy", "inside.txt"));
        Assert.Contains(result.Entries, entry => entry.RelativePath == "top.txt");
        Assert.Equal(2L, result.FileCount);
        Assert.Equal(8L, result.LogicalFileBytes);
    }

    [Fact]
    public void Scan_WhenAListingFailsPartWay_KeepsWhatWasAlreadyObserved()
    {
        using var tree = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithListing(tree.Root, FailingListing);

        IEnumerable<ObservedDirectoryChild> FailingListing()
        {
            yield return Observations.FileItem("before-failure.txt", 2L);
            throw new IOException("The device is not ready.");
        }

        var result = new SourceScanner(reader).Scan(tree.Root);

        Assert.Equal("before-failure.txt", Assert.Single(result.Entries).RelativePath);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(SourceScanIssueKind.CannotEnumerateDirectory, issue.Kind);
        Assert.Null(issue.RelativePath);
        Assert.Equal("System.IO.IOException", issue.ExceptionTypeName);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Scan_WhenAnEntryMetadataCannotBeRead_RecordsAnIssueAndSkipsTheEntry()
    {
        using var tree = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithChildren(
                tree.Root,
                Observations.UnreadableItem("mystery", new UnauthorizedAccessException("Access is denied.")),
                Observations.ItemWithoutMetadata("also-mystery"),
                Observations.FileItem("normal.txt", 4L));

        var result = new SourceScanner(reader).Scan(tree.Root);

        Assert.Equal("normal.txt", Assert.Single(result.Entries).RelativePath);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue =>
        {
            Assert.Equal(SourceScanIssueKind.CannotReadEntryMetadata, issue.Kind);
            Assert.False(string.IsNullOrWhiteSpace(issue.Diagnostic));
        });

        Assert.Equal(
            "System.UnauthorizedAccessException",
            Assert.Single(result.Issues, issue => issue.RelativePath == "mystery").ExceptionTypeName);
        Assert.Null(Assert.Single(result.Issues, issue => issue.RelativePath == "also-mystery").ExceptionTypeName);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Scan_WhenAFileLengthIsUnknown_DoesNotInventAZeroLengthFile()
    {
        using var tree = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithChildren(tree.Root, Observations.FileItemWithoutLength("unknown-size.bin"));

        var result = new SourceScanner(reader).Scan(tree.Root);

        // No entry is produced, so no zero-length file is fabricated and no bytes are counted.
        Assert.Empty(result.Entries);
        Assert.Equal(0L, result.FileCount);
        Assert.Equal(0L, result.LogicalFileBytes);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(SourceScanIssueKind.CannotReadEntryMetadata, issue.Kind);
        Assert.Equal("unknown-size.bin", issue.RelativePath);
    }

    [Fact]
    public void Scan_WhenNothingFails_ReportsACompleteScan()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile(Path.Combine("sub", "a.txt"), 1);

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Empty(result.Issues);
        Assert.True(result.IsComplete);
        Assert.Equal(result.Entries.Count, result.FileCount + result.DirectoryCount + result.ReparsePointCount);
    }

    // ---------------------------------------------------------------- reparse points

    [Fact]
    public void Scan_OfADirectoryJunction_RecordsItWithoutEnteringIt()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target-directory");
        tree.CreateFile(Path.Combine("target-directory", "hidden.txt"), 7);
        tree.CreateDirectoryJunction("link-to-target", target);

        var result = new SourceScanner().Scan(tree.Root);

        var link = Assert.Single(result.Entries, entry => entry.IsReparsePoint);
        Assert.Equal("link-to-target", link.RelativePath);
        Assert.Equal(SourceEntryKind.Directory, link.Kind);
        Assert.Null(link.LengthInBytes);

        // The link target was not walked, so its contents appear once, under their real path only.
        Assert.DoesNotContain(
            result.Entries,
            entry => entry.RelativePath.StartsWith("link-to-target" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        Assert.Equal(1L, result.FileCount);
        Assert.Equal(1L, result.DirectoryCount);
        Assert.Equal(1L, result.ReparsePointCount);
        Assert.Equal(7L, result.LogicalFileBytes);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Scan_OfADirectoryJunctionToAnExternalDirectory_DoesNotObserveTheTargetContents()
    {
        using var tree = new TemporarySourceTree();
        var external = tree.CreateExternalDirectory();
        tree.CreateExternalFile(external, "outside.txt", 11);
        tree.CreateDirectoryJunction("link-to-outside", external);

        var result = new SourceScanner().Scan(tree.Root);

        Assert.Equal(0L, result.FileCount);
        Assert.Equal(0L, result.DirectoryCount);
        Assert.Equal(1L, result.ReparsePointCount);
        Assert.Equal(0L, result.LogicalFileBytes);
    }

    [Fact]
    public void Scan_OfAFileShapedReparsePoint_RecordsItWithoutAttributingALength()
    {
        using var tree = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithChildren(tree.Root, Observations.ReparsePointItem("placeholder.bin", directoryShaped: false));

        var result = new SourceScanner(reader).Scan(tree.Root);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(SourceEntryKind.File, entry.Kind);
        Assert.True(entry.IsReparsePoint);

        // The 4096 bytes the listing reported are deliberately not attributed: they may describe the
        // link target rather than the link, and nothing here resolved the link to find out.
        Assert.Null(entry.LengthInBytes);
        Assert.Equal(0L, result.FileCount);
        Assert.Equal(1L, result.ReparsePointCount);
        Assert.Equal(0L, result.LogicalFileBytes);
    }

    [Fact]
    public void Scan_OfADirectoryShapedReparsePoint_DoesNotQueueItForWalking()
    {
        using var tree = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithChildren(
                tree.Root,
                Observations.ReparsePointItem("link", directoryShaped: true),
                Observations.FileItem("normal.txt", 1L));

        var result = new SourceScanner(reader).Scan(tree.Root);

        var link = Assert.Single(result.Entries, entry => entry.IsReparsePoint);
        Assert.Equal(SourceEntryKind.Directory, link.Kind);
        Assert.Equal(1L, result.ReparsePointCount);
        Assert.Equal(1L, result.FileCount);

        // Only the root was ever listed: the reparse point was recorded and left alone.
        Assert.Equal([tree.Root], reader.ListedDirectories);
    }

    // ---------------------------------------------------------------- progress

    [Fact]
    public void Scan_ReportsProgressAtLeastOnceEvenForAnEmptyRoot()
    {
        using var tree = new TemporarySourceTree();
        var progress = new RecordingProgress();

        var result = new SourceScanner().Scan(tree.Root, progress);

        var snapshot = Assert.Single(progress.Snapshots);
        Assert.Equal(0L, snapshot.EntriesObserved);
        Assert.Equal(0L, snapshot.FilesObserved);
        Assert.Equal(0L, snapshot.DirectoriesObserved);
        Assert.Equal(0L, snapshot.ReparsePointsObserved);
        Assert.Equal(0L, snapshot.LogicalBytesObserved);
        Assert.Equal(0L, snapshot.IssuesObserved);
        Assert.Null(snapshot.CurrentRelativePath);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Scan_ReportsProgressThatNeverGoesBackwardsAndEndsMatchingTheResult()
    {
        using var tree = new TemporarySourceTree();
        tree.CreateFile("a.txt", 3);
        tree.CreateFile(Path.Combine("sub", "b.bin"), 5);
        tree.CreateDirectory(Path.Combine("sub", "deeper"));
        tree.CreateFile(Path.Combine("sub", "deeper", "c.dat"), 7);

        var progress = new RecordingProgress();
        var result = new SourceScanner().Scan(tree.Root, progress);

        Assert.NotEmpty(progress.Snapshots);

        for (var index = 0; index < progress.Snapshots.Count; index++)
        {
            var snapshot = progress.Snapshots[index];

            Assert.True(snapshot.EntriesObserved >= 0);
            Assert.True(snapshot.FilesObserved >= 0);
            Assert.True(snapshot.DirectoriesObserved >= 0);
            Assert.True(snapshot.ReparsePointsObserved >= 0);
            Assert.True(snapshot.LogicalBytesObserved >= 0);
            Assert.True(snapshot.IssuesObserved >= 0);

            if (index == 0)
            {
                continue;
            }

            var previous = progress.Snapshots[index - 1];
            Assert.True(snapshot.EntriesObserved >= previous.EntriesObserved);
            Assert.True(snapshot.FilesObserved >= previous.FilesObserved);
            Assert.True(snapshot.DirectoriesObserved >= previous.DirectoriesObserved);
            Assert.True(snapshot.ReparsePointsObserved >= previous.ReparsePointsObserved);
            Assert.True(snapshot.LogicalBytesObserved >= previous.LogicalBytesObserved);
            Assert.True(snapshot.IssuesObserved >= previous.IssuesObserved);
        }

        var last = progress.Snapshots[^1];
        Assert.Equal((long)result.Entries.Count, last.EntriesObserved);
        Assert.Equal(result.FileCount, last.FilesObserved);
        Assert.Equal(result.DirectoryCount, last.DirectoriesObserved);
        Assert.Equal(result.ReparsePointCount, last.ReparsePointsObserved);
        Assert.Equal(result.LogicalFileBytes, last.LogicalBytesObserved);
        Assert.Equal((long)result.Issues.Count, last.IssuesObserved);
    }

    private static IReadOnlyList<string> Canonical(IEnumerable<string> paths)
        => paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
}

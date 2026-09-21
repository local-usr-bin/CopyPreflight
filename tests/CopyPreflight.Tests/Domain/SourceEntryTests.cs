using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The minimal source fact model: what the scanner observed, and nothing more.
/// </summary>
public class SourceEntryTests
{
    [Fact]
    public void FileCarriesItsRelativePathAndLength()
    {
        var entry = SourceEntry.File(@"photos\a.jpg", 1_234_567L);

        Assert.Equal(@"photos\a.jpg", entry.RelativePath);
        Assert.Equal(SourceEntryKind.File, entry.Kind);
        Assert.Equal(1_234_567L, entry.LengthInBytes!.Value);
    }

    [Fact]
    public void FileAcceptsAZeroLength()
    {
        var entry = SourceEntry.File("empty.txt", 0L);

        Assert.Equal(0L, entry.LengthInBytes!.Value);
    }

    [Fact]
    public void DirectoryDoesNotInventAFileLength()
    {
        var entry = SourceEntry.Directory("photos");

        Assert.Equal("photos", entry.RelativePath);
        Assert.Equal(SourceEntryKind.Directory, entry.Kind);
        Assert.Null(entry.LengthInBytes);
    }

    [Fact]
    public void LengthsAreCarriedOnlyByFiles()
    {
        Assert.NotNull(SourceEntry.File("a.txt", 1L).LengthInBytes);
        Assert.Null(SourceEntry.Directory("a").LengthInBytes);
    }

    [Fact]
    public void RejectsANegativeFileLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = SourceEntry.File("a.txt", -1L); });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsAMissingPath(string? path)
    {
        Assert.Throws<ArgumentException>(() => { _ = SourceEntry.File(path!, 1L); });
        Assert.Throws<ArgumentException>(() => { _ = SourceEntry.Directory(path!); });
    }

    [Fact]
    public void IsValueEqualWhenKindPathAndLengthMatch()
    {
        Assert.Equal(SourceEntry.File("a.txt", 3L), SourceEntry.File("a.txt", 3L));
        Assert.NotEqual(SourceEntry.File("a.txt", 3L), SourceEntry.File("a.txt", 4L));
        Assert.NotEqual(SourceEntry.File("a", 0L), SourceEntry.Directory("a"));
    }
}

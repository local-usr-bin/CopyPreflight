using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Semantics of the open file system name.
/// </summary>
public class FileSystemNameTests
{
    [Fact]
    public void PreservesTheReportedName()
    {
        var name = new FileSystemName("NTFS");

        Assert.Equal("NTFS", name.Value);
        Assert.Equal("NTFS", name.ToString());
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("exFAT", new FileSystemName("  exFAT  ").Value);
    }

    [Fact]
    public void AcceptsNamesOutsideTheWellKnownSet()
    {
        // File system names are an open set: an unrecognized name is still a known name.
        var name = new FileSystemName("SomeVendorFS");

        Assert.Equal("SomeVendorFS", name.Value);
    }

    [Fact]
    public void ComparesNamesCaseInsensitively()
    {
        var upper = new FileSystemName("NTFS");
        var lower = new FileSystemName("ntfs");

        Assert.Equal(upper, lower);
        Assert.True(upper == lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
        Assert.False(upper != lower);
    }

    [Fact]
    public void DistinguishesDifferentNames()
    {
        Assert.NotEqual(new FileSystemName("NTFS"), new FileSystemName("FAT32"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingText(string? value)
    {
        Assert.Throws<ArgumentException>(() => { _ = new FileSystemName(value!); });
    }
}

using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Identity semantics of a rule / finding code.
/// </summary>
public class FindingCodeTests
{
    [Fact]
    public void KeepsItsTextAndIsStableAcrossInstances()
    {
        var code = new FindingCode("FAT32_MAX_FILE_SIZE");

        Assert.Equal("FAT32_MAX_FILE_SIZE", code.Value);
        Assert.Equal("FAT32_MAX_FILE_SIZE", code.ToString());
        Assert.Equal(code, new FindingCode("FAT32_MAX_FILE_SIZE"));
        Assert.Equal(code.GetHashCode(), new FindingCode("FAT32_MAX_FILE_SIZE").GetHashCode());
    }

    [Fact]
    public void ComparesCodesCaseSensitively()
    {
        Assert.NotEqual(new FindingCode("PATH_TOO_LONG"), new FindingCode("path_too_long"));
    }

    [Fact]
    public void DoesNotRewriteTheTextItWasGiven()
    {
        // Codes are identifiers a rule chooses; this type normalizes nothing.
        Assert.Equal("CODE ", new FindingCode("CODE ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingText(string? value)
    {
        Assert.Throws<ArgumentException>(() => { _ = new FindingCode(value!); });
    }
}

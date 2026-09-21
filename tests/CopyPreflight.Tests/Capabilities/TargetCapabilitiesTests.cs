using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// The target capability set must express known and unknown capabilities side by side.
/// </summary>
public class TargetCapabilitiesTests
{
    [Fact]
    public void ExpressesKnownAndUnknownCapabilitiesAtTheSameTime()
    {
        var capabilities = new TargetCapabilities(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Unknown("free space was not queried"),
            CapabilityValue<int>.Known(255));

        Assert.True(capabilities.FileSystemName.IsKnown);
        Assert.Equal(new FileSystemName("NTFS"), capabilities.FileSystemName.Value);

        Assert.True(capabilities.AvailableFreeBytes.IsUnknown);
        Assert.Equal("free space was not queried", capabilities.AvailableFreeBytes.Diagnostic);

        Assert.True(capabilities.MaxPathComponentLength.IsKnown);
        Assert.Equal(255, capabilities.MaxPathComponentLength.Value);
    }

    [Fact]
    public void AllowsATargetAboutWhichNothingIsKnown()
    {
        var capabilities = new TargetCapabilities(
            CapabilityValue<FileSystemName>.Unknown(),
            CapabilityValue<long>.Unknown(),
            CapabilityValue<int>.Unknown());

        Assert.True(capabilities.FileSystemName.IsUnknown);
        Assert.True(capabilities.AvailableFreeBytes.IsUnknown);
        Assert.True(capabilities.MaxPathComponentLength.IsUnknown);
    }

    [Fact]
    public void UnknownFreeSpaceIsNotTheSameAsZeroFreeSpace()
    {
        var unknown = Build(freeBytes: CapabilityValue<long>.Unknown());
        var zero = Build(freeBytes: CapabilityValue<long>.Known(0));

        Assert.NotEqual(zero, unknown);
    }

    [Fact]
    public void RejectsAKnownNegativeFreeSpace()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = Build(freeBytes: CapabilityValue<long>.Known(-1));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsAKnownNonPositivePathComponentLength(int maxPathComponentLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = Build(maxPathComponentLength: CapabilityValue<int>.Known(maxPathComponentLength));
        });
    }

    [Fact]
    public void ComparesFileSystemNamesCaseInsensitively()
    {
        var upper = Build(fileSystemName: CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")));
        var lower = Build(fileSystemName: CapabilityValue<FileSystemName>.Known(new FileSystemName("ntfs")));

        Assert.Equal(upper, lower);
    }

    private static TargetCapabilities Build(
        CapabilityValue<FileSystemName>? fileSystemName = null,
        CapabilityValue<long>? freeBytes = null,
        CapabilityValue<int>? maxPathComponentLength = null)
        => new(
            fileSystemName ?? CapabilityValue<FileSystemName>.Unknown(),
            freeBytes ?? CapabilityValue<long>.Unknown(),
            maxPathComponentLength ?? CapabilityValue<int>.Unknown());
}

using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The target probe against the real Windows file system.
/// </summary>
/// <remarks>
/// <para>
/// These tests use only paths that exist on any Windows machine — the process temp directory and
/// paths below it. They require no network share, no quota, no mounted volume, no elevation and no
/// Developer Mode, and they never hardcode which file system the test machine happens to use.
/// </para>
/// <para>
/// The partial-success semantics that matter most for network targets cannot be produced here, because
/// that needs a share that refuses volume information; they are covered deterministically through the
/// query seam instead. See <see cref="WindowsTargetCapabilityProbeTests"/>.
/// </para>
/// </remarks>
public class WindowsTargetCapabilityProbeIntegrationTests
{
    [Fact]
    public void ReportsTheThreeCapabilitiesOfARealDirectory()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");

        var result = new WindowsTargetCapabilityProbe().Probe(target);

        Assert.Equal(Path.GetFullPath(target), result.TargetDirectoryPath);
        Assert.Empty(result.Issues);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsKnown);
        Assert.True(result.Capabilities.AvailableFreeBytes.Value >= 0);

        Assert.True(result.Capabilities.FileSystemName.IsKnown);
        Assert.False(string.IsNullOrWhiteSpace(result.Capabilities.FileSystemName.Value.Value));

        Assert.True(result.Capabilities.MaxPathComponentLength.IsKnown);
        Assert.True(result.Capabilities.MaxPathComponentLength.Value > 0);
    }

    /// <summary>
    /// Probing is read-only. A directory that was empty before the probe must still be empty after it,
    /// because CopyPreflight never writes a probe file to establish anything.
    /// </summary>
    [Fact]
    public void WritesNothingIntoTheTargetDirectory()
    {
        using var tree = new TemporarySourceTree();
        var target = tree.CreateDirectory("target");

        Assert.Empty(Directory.EnumerateFileSystemEntries(target));

        new WindowsTargetCapabilityProbe().Probe(target);

        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    /// <summary>
    /// A target that does not exist is reported, not created and not thrown at the caller.
    /// </summary>
    [Fact]
    public void ReportsAPathThatDoesNotExistWithoutCreatingIt()
    {
        using var tree = new TemporarySourceTree();
        var missingTarget = tree.PathOf("never-created");

        var result = new WindowsTargetCapabilityProbe().Probe(missingTarget);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathUnavailable, issue.Kind);
        Assert.NotNull(issue.NativeErrorCode);
        Assert.Equal("GetFileAttributesW", issue.Operation);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
        Assert.True(result.Capabilities.AvailableFreeBytes.IsUnknown);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);

        Assert.False(Directory.Exists(missingTarget));
        Assert.False(File.Exists(missingTarget));
    }

    /// <summary>
    /// A path whose name Windows refuses is reported as an invalid target path rather than as an
    /// unreachable one. The path used here contains a character Windows does not allow in a name.
    /// </summary>
    [Fact]
    public void ReportsAMalformedTargetPathAsInvalid()
    {
        var result = new WindowsTargetCapabilityProbe().Probe(@"C:\cp-invalid-<x>|y");

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathInvalid, issue.Kind);
        Assert.Equal(123, issue.NativeErrorCode);
    }

    [Fact]
    public void ReportsAnExistingFileAsAnUnusableTarget()
    {
        using var tree = new TemporarySourceTree();
        var file = tree.CreateFile("not-a-directory.txt");

        var result = new WindowsTargetCapabilityProbe().Probe(file);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathUnavailable, issue.Kind);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsUnknown);
    }

    /// <summary>
    /// The probe is Windows-specific and says so, so a caller on another platform is told by the type
    /// system rather than by discovering it at run time.
    /// </summary>
    [Fact]
    public void DeclaresItsWindowsPlatformContract()
    {
        var platforms = typeof(WindowsTargetCapabilityProbe)
            .GetCustomAttributes<SupportedOSPlatformAttribute>()
            .Select(attribute => attribute.PlatformName)
            .ToList();

        Assert.Contains("windows", platforms);
    }
}

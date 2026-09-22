using System.IO;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The target probe must establish each capability on its own, keep whatever it did establish, and
/// leave everything else explicitly unknown.
/// </summary>
/// <remarks>
/// These tests drive the probe through its Windows query seam, so the outcomes a developer machine
/// cannot produce honestly — a volume that answers the free-space question but not the
/// volume-information one, and native values no real volume reports — can be exercised deterministically.
/// </remarks>
public class WindowsTargetCapabilityProbeTests
{
    private const string TargetPath = @"C:\target";

    [Fact]
    public void ReportsEveryCapabilityWindowsAnswered()
    {
        var api = new ScriptedFileSystemApi()
            .WithFreeBytesAvailableToCaller(4_294_967_296)
            .WithVolumeInformation("ReFS", 255);

        var result = Probe(api);

        Assert.Empty(result.Issues);
        Assert.Equal(Path.GetFullPath(TargetPath), result.TargetDirectoryPath);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsKnown);
        Assert.Equal(4_294_967_296L, result.Capabilities.AvailableFreeBytes.Value);

        Assert.True(result.Capabilities.FileSystemName.IsKnown);
        Assert.Equal("ReFS", result.Capabilities.FileSystemName.Value.Value);

        Assert.True(result.Capabilities.MaxPathComponentLength.IsKnown);
        Assert.Equal(255, result.Capabilities.MaxPathComponentLength.Value);
    }

    [Fact]
    public void ResolvesARelativeTargetAgainstTheProcessDirectory()
    {
        var result = Probe(new ScriptedFileSystemApi(), "relative-target");

        Assert.Equal(Path.GetFullPath("relative-target"), result.TargetDirectoryPath);
    }

    /// <summary>
    /// The behaviour M3 exists to get right: a network target can answer the free-space question while
    /// refusing or failing the volume-information one. That is a legitimate result, not a failure.
    /// </summary>
    [Fact]
    public void KeepsFreeSpaceWhenTheVolumeInformationQueryFails()
    {
        var api = new ScriptedFileSystemApi()
            .WithFreeBytesAvailableToCaller(987_654_321)
            .WithVolumeInformationFailure(ScriptedFileSystemApi.ErrorAccessDenied);

        var result = Probe(api);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsKnown);
        Assert.Equal(987_654_321L, result.Capabilities.AvailableFreeBytes.Value);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.VolumeInformationQueryFailed, issue.Kind);
    }

    [Fact]
    public void KeepsTheVolumeCapabilitiesWhenTheFreeSpaceQueryFails()
    {
        var api = new ScriptedFileSystemApi()
            .WithFreeSpaceFailure(ScriptedFileSystemApi.ErrorAccessDenied)
            .WithVolumeInformation("exFAT", 255);

        var result = Probe(api);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsUnknown);

        Assert.True(result.Capabilities.FileSystemName.IsKnown);
        Assert.Equal("exFAT", result.Capabilities.FileSystemName.Value.Value);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsKnown);
        Assert.Equal(255, result.Capabilities.MaxPathComponentLength.Value);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.FreeSpaceQueryFailed, issue.Kind);
    }

    [Fact]
    public void DoesNotGuessAVolumeRootWhenTheVolumePathQueryFails()
    {
        var api = new ScriptedFileSystemApi()
            .WithFreeBytesAvailableToCaller(1_000)
            .WithVolumePathFailure(ScriptedFileSystemApi.ErrorBadNetName);

        var result = Probe(api);

        Assert.True(result.Capabilities.AvailableFreeBytes.IsKnown);
        Assert.Equal(1_000L, result.Capabilities.AvailableFreeBytes.Value);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.VolumePathQueryFailed, issue.Kind);

        // No root was invented: the volume information query was never attempted.
        Assert.Empty(api.VolumeInformationPaths);
    }

    [Fact]
    public void LeavesTheVolumeUnknownWhenWindowsReportsNoUsableVolumePath()
    {
        // A call that succeeds but answers nothing is still no volume path.
        var api = new ScriptedFileSystemApi().WithVolumePathFailure(nativeError: 0);

        var result = Probe(api);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.VolumePathQueryFailed, issue.Kind);

        // There was no native error to preserve, so none is invented.
        Assert.Null(issue.NativeErrorCode);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);
    }

    [Fact]
    public void RecordsTheOperationAndNativeErrorBehindAFailedQuery()
    {
        var api = new ScriptedFileSystemApi()
            .WithFreeSpaceFailure(ScriptedFileSystemApi.ErrorBadNetName);

        var result = Probe(api);

        var issue = Assert.Single(result.Issues);

        Assert.Equal(TargetProbeIssueKind.FreeSpaceQueryFailed, issue.Kind);
        Assert.Equal("GetDiskFreeSpaceExW", issue.Operation);
        Assert.Equal(ScriptedFileSystemApi.ErrorBadNetName, issue.NativeErrorCode);
        Assert.Equal(@"C:\target\", issue.AffectedPath);
        Assert.False(string.IsNullOrWhiteSpace(issue.Diagnostic));
        Assert.Contains(
            ScriptedFileSystemApi.ErrorBadNetName.ToString(System.Globalization.CultureInfo.InvariantCulture),
            issue.Diagnostic,
            StringComparison.Ordinal);

        // An unknown capability explains itself, too.
        Assert.False(string.IsNullOrWhiteSpace(result.Capabilities.AvailableFreeBytes.Diagnostic));
    }

    /// <summary>
    /// The probe must use the bytes available <b>to the calling user</b>, which is the quota-aware
    /// figure a migration depends on. The seam offers no total-free-bytes channel at all, so there is
    /// nothing else the probe could be reading, and the reported figure is carried through unchanged.
    /// </summary>
    [Fact]
    public void UsesTheBytesAvailableToTheCaller()
    {
        const ulong AvailableToCaller = 7_777_777_777;

        var result = Probe(new ScriptedFileSystemApi().WithFreeBytesAvailableToCaller(AvailableToCaller));

        Assert.True(result.Capabilities.AvailableFreeBytes.IsKnown);
        Assert.Equal(AvailableToCaller, (ulong)result.Capabilities.AvailableFreeBytes.Value);
    }

    /// <summary>
    /// The native quantity is unsigned while the capability model stores a signed amount. A value the
    /// model cannot represent must not silently become a wrong number.
    /// </summary>
    [Fact]
    public void LeavesFreeSpaceUnknownInsteadOfTruncatingANativeValue()
    {
        var result = Probe(new ScriptedFileSystemApi().WithFreeBytesAvailableToCaller(ulong.MaxValue));

        Assert.True(result.Capabilities.AvailableFreeBytes.IsUnknown);

        var diagnostic = result.Capabilities.AvailableFreeBytes.Diagnostic;

        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
        Assert.Contains(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), diagnostic, StringComparison.Ordinal);

        // The query itself did not fail, so there is no failure to report.
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DoesNotTreatAZeroComponentLengthAsAKnownLength()
    {
        var result = Probe(new ScriptedFileSystemApi().WithVolumeInformation("NTFS", 0));

        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);
        Assert.False(string.IsNullOrWhiteSpace(result.Capabilities.MaxPathComponentLength.Diagnostic));

        // The file system name answered by the same call is unaffected.
        Assert.True(result.Capabilities.FileSystemName.IsKnown);
    }

    [Fact]
    public void DoesNotTreatAnUnrepresentableComponentLengthAsAKnownLength()
    {
        var result = Probe(new ScriptedFileSystemApi().WithVolumeInformation("NTFS", uint.MaxValue));

        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);

        var diagnostic = result.Capabilities.MaxPathComponentLength.Diagnostic;

        Assert.False(string.IsNullOrWhiteSpace(diagnostic));
        Assert.Contains(uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), diagnostic, StringComparison.Ordinal);

        Assert.True(result.Capabilities.FileSystemName.IsKnown);
    }

    /// <summary>
    /// The volume must come from Windows' mount point resolution, not from taking the path root: a
    /// target can live on a mounted folder, where the two answers differ.
    /// </summary>
    [Fact]
    public void DescribesTheVolumeWindowsReportedRatherThanTheTargetPathRoot()
    {
        var api = new ScriptedFileSystemApi()
            .WithVolumePath(@"D:\Mounted\Publications\")
            .WithVolumeInformation("NTFS", 255);

        var result = Probe(api);

        Assert.Equal([@"D:\Mounted\Publications\"], api.VolumeInformationPaths);
        Assert.Equal([TargetPath], api.VolumePathPaths);

        Assert.True(result.Capabilities.FileSystemName.IsKnown);
        Assert.Equal("NTFS", result.Capabilities.FileSystemName.Value.Value);
    }

    /// <summary>
    /// Windows documents that a UNC name must carry a trailing backslash where a local directory path
    /// need not, so the probe normalizes the separator for every target and never rewrites a UNC path
    /// into a drive path.
    /// </summary>
    [Fact]
    public void AppendsTheSeparatorWindowsRequiresForANetworkTarget()
    {
        const string UncTarget = @"\\server\share";

        var api = new ScriptedFileSystemApi().WithVolumePath(@"\\server\share\");

        var result = Probe(api, UncTarget);

        Assert.Equal(UncTarget, result.TargetDirectoryPath);
        Assert.Equal(UncTarget, Assert.Single(api.AttributePaths));
        Assert.Equal(UncTarget, Assert.Single(api.VolumePathPaths));
        Assert.Equal(@"\\server\share\", Assert.Single(api.FreeSpacePaths));
    }

    [Fact]
    public void DoesNotRepeatTheSeparatorOnAVolumeRoot()
    {
        var api = new ScriptedFileSystemApi().WithVolumePath(@"C:\");

        var result = Probe(api, @"C:\");

        Assert.Equal(@"C:\", result.TargetDirectoryPath);
        Assert.Equal(@"C:\", Assert.Single(api.FreeSpacePaths));
        Assert.Equal(@"C:\", Assert.Single(api.VolumeInformationPaths));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsANullEmptyOrWhitespaceTarget(string? targetDirectoryPath)
    {
        var api = new ScriptedFileSystemApi();

        Assert.Throws<ArgumentException>(() => new WindowsTargetCapabilityProbe(api).Probe(targetDirectoryPath!));

        // Nothing was asked of Windows.
        Assert.Empty(api.AttributePaths);
    }

    [Fact]
    public void ReportsAnUnreachableTargetWithoutThrowing()
    {
        var api = new ScriptedFileSystemApi().WithAttributeFailure(ScriptedFileSystemApi.ErrorPathNotFound);

        var result = Probe(api);

        Assert.Equal(Path.GetFullPath(TargetPath), result.TargetDirectoryPath);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
        Assert.True(result.Capabilities.AvailableFreeBytes.IsUnknown);
        Assert.True(result.Capabilities.MaxPathComponentLength.IsUnknown);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathUnavailable, issue.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorPathNotFound, issue.NativeErrorCode);
        Assert.Equal("GetFileAttributesW", issue.Operation);
    }

    /// <summary>
    /// A name Windows refuses outright is a different fact from a name that is well formed but leads
    /// nowhere, and the two are reported apart rather than collapsed into one state.
    /// </summary>
    [Fact]
    public void ReportsAMalformedNameAsInvalidRatherThanUnavailable()
    {
        var api = new ScriptedFileSystemApi().WithAttributeFailure(ScriptedFileSystemApi.ErrorInvalidName);

        var result = Probe(api);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathInvalid, issue.Kind);
        Assert.Equal(ScriptedFileSystemApi.ErrorInvalidName, issue.NativeErrorCode);
        Assert.NotEqual(TargetProbeIssueKind.TargetPathUnavailable, issue.Kind);
    }

    [Fact]
    public void ReportsAnExistingObjectThatIsNotADirectoryAsAnUnusableTarget()
    {
        var api = new ScriptedFileSystemApi().WithAttributes(FileAttributes.Archive);

        var result = Probe(api);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(TargetProbeIssueKind.TargetPathUnavailable, issue.Kind);
        Assert.Null(issue.NativeErrorCode);

        Assert.True(result.Capabilities.FileSystemName.IsUnknown);
    }

    [Fact]
    public void DoesNotQueryTheVolumeOfATargetThatIsNotAUsableDirectory()
    {
        var api = new ScriptedFileSystemApi().WithAttributeFailure(ScriptedFileSystemApi.ErrorFileNotFound);

        Probe(api);

        Assert.Empty(api.FreeSpacePaths);
        Assert.Empty(api.VolumePathPaths);
        Assert.Empty(api.VolumeInformationPaths);
    }

    private static TargetProbeResult Probe(ScriptedFileSystemApi api, string targetDirectoryPath = TargetPath)
        => new WindowsTargetCapabilityProbe(api).Probe(targetDirectoryPath);
}

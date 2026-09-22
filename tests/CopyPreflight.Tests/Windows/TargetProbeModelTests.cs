using System.Reflection;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The probe's result model must stay a plain, inspectable description of what was observed: it
/// carries no verdict, and its validators cannot be side-stepped from outside the assembly.
/// </summary>
public class TargetProbeModelTests
{
    [Fact]
    public void AProbeIssueCarriesTheEvidenceBehindAFailure()
    {
        var issue = new TargetProbeIssue(
            TargetProbeIssueKind.VolumeInformationQueryFailed,
            @"\\server\share\",
            "GetVolumeInformationW",
            nativeErrorCode: 1231,
            "Windows did not describe the file system of the target volume.");

        Assert.Equal(TargetProbeIssueKind.VolumeInformationQueryFailed, issue.Kind);
        Assert.Equal(@"\\server\share\", issue.AffectedPath);
        Assert.Equal("GetVolumeInformationW", issue.Operation);
        Assert.Equal(1231, issue.NativeErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(issue.Diagnostic));
    }

    [Fact]
    public void AProbeIssueMayHaveNoNativeError()
    {
        var issue = new TargetProbeIssue(
            TargetProbeIssueKind.TargetPathUnavailable,
            @"C:\target",
            "GetFileAttributesW",
            nativeErrorCode: null,
            "The target path names an existing object that is not a directory.");

        Assert.Null(issue.NativeErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AProbeIssueRejectsAnEmptyAffectedPath(string affectedPath)
    {
        Assert.Throws<ArgumentException>(() => new TargetProbeIssue(
            TargetProbeIssueKind.TargetPathInvalid,
            affectedPath,
            "GetFileAttributesW",
            nativeErrorCode: null,
            "diagnostic"));
    }

    [Fact]
    public void AProbeIssueRejectsANullAffectedPath()
    {
        Assert.Throws<ArgumentException>(() => new TargetProbeIssue(
            TargetProbeIssueKind.TargetPathInvalid,
            affectedPath: null!,
            "GetFileAttributesW",
            nativeErrorCode: null,
            "diagnostic"));
    }

    [Fact]
    public void AProbeIssueRejectsAnEmptyOperation()
    {
        Assert.Throws<ArgumentException>(() => new TargetProbeIssue(
            TargetProbeIssueKind.TargetPathInvalid,
            @"C:\target",
            operation: "  ",
            nativeErrorCode: null,
            "diagnostic"));
    }

    [Fact]
    public void AProbeIssueRejectsAnEmptyDiagnostic()
    {
        Assert.Throws<ArgumentException>(() => new TargetProbeIssue(
            TargetProbeIssueKind.TargetPathInvalid,
            @"C:\target",
            "GetFileAttributesW",
            nativeErrorCode: null,
            diagnostic: string.Empty));
    }

    [Fact]
    public void AProbeResultRejectsMissingParts()
    {
        var capabilities = new TargetCapabilities(
            CapabilityValue<FileSystemName>.Unknown(),
            CapabilityValue<long>.Unknown(),
            CapabilityValue<int>.Unknown());

        Assert.Throws<ArgumentNullException>(() => new TargetProbeResult(@"C:\target", capabilities, issues: null!));
        Assert.Throws<ArgumentNullException>(() => new TargetProbeResult(@"C:\target", capabilities: null!, []));
        Assert.Throws<ArgumentException>(() => new TargetProbeResult("  ", capabilities, []));
    }

    /// <summary>
    /// Same invariant the core domain model holds to: no public property has a setter, so neither a
    /// <c>with</c> expression nor an object initializer can build an instance that skipped validation.
    /// </summary>
    [Theory]
    [InlineData(typeof(TargetProbeIssue))]
    [InlineData(typeof(TargetProbeResult))]
    public void NoInitializerCanBypassAValidator(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.Null(property.SetMethod);
            Assert.False(property.CanWrite);
        }
    }
}

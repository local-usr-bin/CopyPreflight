using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The verdicts the existing-object facts produce: which combinations are a Warning, which are an
/// Error, and which are deliberately silent.
/// </summary>
/// <remarks>
/// The facts are constructed directly, because these tests are about the judgement and not about the
/// query. That the query actually produces such facts against a real file system is verified separately
/// by <see cref="TargetExistingObjectProbeIntegrationTests"/>.
/// </remarks>
public class TargetExistingObjectRuleTests
{
    [Fact]
    public void AFileOverAnExistingFileIsAWarning()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed("report.txt", TargetObjectPresence.File));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var finding = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingFileCollision, finding.Code);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("report.txt", finding.SourceRelativePath);
        Assert.Contains("report.txt", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryOverAnExistingDirectoryIsNotReportedAtAll()
    {
        // Directories merge rather than clash, and one undifferentiated finding per existing directory
        // would add noise without adding information. The objects inside them are judged on their own.
        var source = ExistingObjectInputs.Source(SourceEntry.Directory("media"));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed("media", TargetObjectPresence.Directory));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Empty(findings);
    }

    [Fact]
    public void AFileOverAnExistingDirectoryIsAnError()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("media", 1024));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed("media", TargetObjectPresence.Directory));

        var finding = Assert.Single(source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe));

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingTypeCollision, finding.Code);
        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void ADirectoryOverAnExistingFileIsAnError()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.Directory("media"));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed("media", TargetObjectPresence.File));

        var finding = Assert.Single(source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe));

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingTypeCollision, finding.Code);
        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void TheTypeConflictSaysTheMappingIsBlockedAndStopsThere()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"docs\a.txt", 1024));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed(@"docs\a.txt", TargetObjectPresence.Directory));

        var finding = Assert.Single(source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe));

        Assert.Contains("structurally blocked", finding.Impact, StringComparison.Ordinal);

        // The clash is described, never resolved: no deleting, no renaming, no re-routing the source.
        Assert.Contains("does not delete or rename", finding.Impact, StringComparison.Ordinal);
        Assert.Contains("does not change the mapping", finding.Impact, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingReparsePointDoesNotEnterTheFileOrDirectoryMatrix()
    {
        // Both a file and a directory map onto the same reparse point here. Neither is reported as a
        // type conflict, because the reparse point is neither an ordinary file nor an ordinary
        // directory and is not classified any further.
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("thing", 1024),
            SourceEntry.Directory("other"));

        var probe = ExistingObjectInputs.ProbeResult(
            Observed("thing", TargetObjectPresence.ReparsePoint),
            Observed("other", TargetObjectPresence.ReparsePoint));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingTypeCollision));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));

        // The reparse point warning is produced once per affected location, as a Warning.
        var reparsePoints = findings.WithCode(WindowsPreflightFindingCodes.TargetExistingReparsePoint);
        Assert.Equal(2, reparsePoints.Count);
        Assert.All(reparsePoints, finding => Assert.Equal(Severity.Warning, finding.Severity));
    }

    [Theory]
    [InlineData(TargetObjectPresence.File)]
    [InlineData(TargetObjectPresence.Directory)]
    public void ASourceReparsePointOverAnExistingObjectIsAWarning(TargetObjectPresence presence)
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.ReparsePoint("link", SourceEntryKind.Directory));

        var probe = ExistingObjectInputs.ProbeResult(Observed("link", presence));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var finding = findings.Only(WindowsPreflightFindingCodes.SourceReparsePointExistingTargetObject);

        Assert.Equal(Severity.Warning, finding.Severity);

        // The source shape is deliberately not turned into a precise type conflict: what a reparse point
        // becomes when it is migrated is not established by this check.
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingTypeCollision));

        // The source reparse point warning of the earlier milestone is kept alongside it.
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.ReparsePointPresent));
    }

    [Fact]
    public void AConfirmedMissingDestinationProducesNoFinding()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("report.txt", 1024),
            SourceEntry.Directory("media"));

        var probe = ExistingObjectInputs.ProbeResult(
            ExistingObjectObservation.ObservedMissing(
                "report.txt",
                ExistingObjectInputs.Mapped("report.txt"),
                ScriptedFileSystemApi.ErrorFileNotFound,
                "Scripted absence."),
            ExistingObjectObservation.InferredMissing(
                "media",
                ExistingObjectInputs.Mapped("media"),
                "media",
                ScriptedFileSystemApi.ErrorFileNotFound,
                "Scripted inferred absence."));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Empty(findings);
    }

    [Fact]
    public void AStructuralBlockIsReportedAtItsCauseEvenWhenTheInventoryDoesNotListIt()
    {
        // An inventory names objects, not the directories they sit in, so this path runs through a
        // directory no entry mentions — and the target holds an ordinary file where that directory
        // would go. The mapping is structurally blocked, so a run that reported nothing here would be
        // presenting a blocked mapping as an unremarkable one.
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"Photos\2026\a.jpg", 1));

        var (probe, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("Photos"), FileAttributes.Normal));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var conflict = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingTypeCollision, conflict.Code);
        Assert.Equal(Severity.Error, conflict.Severity);
        Assert.Equal("Photos", conflict.SourceRelativePath);

        // The cause is what is reported, so it is stated once and the branch behind it is not also
        // presented as a set of locations nobody looked at.
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
    }

    [Fact]
    public void AStructuralBlockAtAnAncestorDoesNotStopAnIndependentBranch()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File(@"Photos\2026\a.jpg", 1),
            SourceEntry.File("report.txt", 1));

        var (probe, _) = ExistingObjectInputs.Run(
            source,
            api => api
                .WithAttribute(ExistingObjectInputs.TargetRoot, FileAttributes.Directory)
                .WithAttribute(ExistingObjectInputs.Mapped("Photos"), FileAttributes.Normal)
                .WithAttributeFailureFor(
                    ExistingObjectInputs.Mapped("report.txt"),
                    ScriptedFileSystemApi.ErrorAccessDenied));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var conflict = findings.Only(WindowsPreflightFindingCodes.TargetExistingTypeCollision);
        Assert.Equal("Photos", conflict.SourceRelativePath);

        // A branch stopped by a confirmed structural conflict is a finished conclusion, so it is not a
        // coverage gap; the location whose own check failed is a genuine gap and is still reported as
        // one. Neither fact is allowed to hide the other.
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void AnExistingFileOnADirectoryTheInventoryOnlyRunsThroughIsATypeConflict()
    {
        // The inventory lists only the object inside the directory, so the directory itself has no
        // entry. It is a directory in the source all the same — a path cannot run through a file — and
        // the file sitting on it is a structural conflict rather than a location with nothing to say.
        var source = ExistingObjectInputs.Source(SourceEntry.File(@"media\2026\a.jpg", 1024));

        var probe = ExistingObjectInputs.ProbeResult(
            Observed("media", TargetObjectPresence.File));

        var finding = Assert.Single(source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe));

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingTypeCollision, finding.Code);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal("media", finding.SourceRelativePath);
    }

    [Fact]
    public void ACollisionIsNeverDescribedAsAMatchOrAsACertainReplacement()
    {
        // Nothing about either file's content was read, so no finding may claim the two are the same,
        // that skipping one is safe, or that a replacement will happen.
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("report.txt", 1024),
            SourceEntry.File("media", 2048),
            SourceEntry.Directory("notes"));

        var probe = ExistingObjectInputs.ProbeResult(
            Observed("report.txt", TargetObjectPresence.File),
            Observed("media", TargetObjectPresence.Directory),
            Observed("notes", TargetObjectPresence.File));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Equal(3, findings.Count);

        AssertNoOverclaiming(findings);
    }

    [Fact]
    public void ARejectedTargetProducesNoDestinationFindings()
    {
        // The target's rejection is already a blocking Error that says no target-dependent check could
        // be carried out, so the existing-object facts are not restated on top of it.
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));
        var probe = ExistingObjectInputs.ProbeResult(
            Observed("report.txt", TargetObjectPresence.File));

        var findings = source.EvaluateWith(
            ExistingObjectInputs.RejectedTarget(),
            probe);

        var finding = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetPathUnavailable, finding.Code);
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));
    }

    [Fact]
    public void ATargetRootReparsePointIsAWarningThatCoversTheWholeTarget()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024));

        var probe = ExistingObjectInputs.ProbeResult(
            TargetRootObservation.Answered(
                TargetRootObservationKind.ReparsePoint,
                "Scripted reparse point at the root."),
            ExistingObjectObservation.NotQueried(
                "report.txt",
                ExistingObjectGapReason.BlockedByReparsePoint,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                "Scripted boundary.",
                ExistingObjectInputs.Mapped("report.txt")));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var reparsePoint = findings.Only(WindowsPreflightFindingCodes.TargetExistingReparsePoint);

        Assert.Equal(Severity.Warning, reparsePoint.Severity);
        Assert.Null(reparsePoint.SourceRelativePath);

        // The coverage of the locations beneath it is stated once, as an Info, rather than per object.
        var coverage = findings.Only(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete);
        Assert.Equal(Severity.Info, coverage.Severity);
        Assert.Null(coverage.SourceRelativePath);
    }

    internal static void AssertNoOverclaiming(IReadOnlyList<Finding> findings)
    {
        string[] forbidden =
        [
            "identical",
            "safe to skip",
            "guaranteed",
            "will fail",
            "cannot be copied",
            "automatically",
        ];

        foreach (var finding in findings)
        {
            var text = string.Join(
                " ",
                finding.Title,
                finding.Explanation,
                finding.Impact,
                finding.Recommendation);

            foreach (var phrase in forbidden)
            {
                Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static ExistingObjectObservation Observed(string relativePath, TargetObjectPresence presence)
        => ExistingObjectObservation.Observed(
            relativePath,
            ExistingObjectInputs.Mapped(relativePath),
            presence,
            "Scripted observation.");
}

using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// How an incomplete check is reported: not performed, partially covered, and cancelled are three
/// different facts, and none of them may be presented as a clean result.
/// </summary>
public class TargetExistingObjectCoverageTests
{
    [Fact]
    public void ACompleteRunOverAFullInventoryAddsNothing()
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
            ExistingObjectObservation.Observed(
                "media",
                ExistingObjectInputs.Mapped("media"),
                TargetObjectPresence.Directory,
                "Scripted observation."));

        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, probe.Outcome);
        Assert.True(probe.IsComplete);
        Assert.Empty(source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe));
    }

    [Fact]
    public void APartialRunIsSummarisedOnceInsteadOfOncePerAffectedObject()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("answered.txt", 1),
            SourceEntry.File("denied.txt", 1),
            SourceEntry.File(@"link\inside.txt", 1),
            SourceEntry.File("odd<.txt", 1));

        var observations = new ExistingObjectObservation[]
        {
            ExistingObjectObservation.Observed(
                "answered.txt",
                ExistingObjectInputs.Mapped("answered.txt"),
                TargetObjectPresence.File,
                "Scripted observation."),
            ExistingObjectObservation.CheckFailed(
                "denied.txt",
                ExistingObjectInputs.Mapped("denied.txt"),
                ScriptedFileSystemApi.ErrorAccessDenied,
                "Scripted failure."),
            ExistingObjectObservation.NotQueried(
                @"link\inside.txt",
                ExistingObjectGapReason.BlockedByReparsePoint,
                "link",
                nativeErrorCode: null,
                "Scripted boundary.",
                ExistingObjectInputs.Mapped(@"link\inside.txt")),
            ExistingObjectObservation.NotQueried(
                "odd<.txt",
                ExistingObjectGapReason.AmbiguousName,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                "Scripted name.",
                ExistingObjectInputs.Mapped("odd<.txt")),
        };

        var probe = ExistingObjectInputs.ProbeResult(observations);
        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        // One Warning for the collision the run did establish, one Warning for the location whose own
        // check did not answer, and one Info for everything the run left uncovered — never one Info per
        // affected object.
        var failed = findings.Only(WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed);
        Assert.Equal(Severity.Warning, failed.Severity);
        Assert.Equal("denied.txt", failed.SourceRelativePath);

        var collision = findings.Only(WindowsPreflightFindingCodes.TargetExistingFileCollision);
        Assert.Equal(Severity.Warning, collision.Severity);
        Assert.Equal("answered.txt", collision.SourceRelativePath);

        var coverage = findings.Only(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete);

        Assert.Equal(Severity.Info, coverage.Severity);
        Assert.Null(coverage.SourceRelativePath);
        Assert.Contains("3 of the 4 mapped locations", coverage.Explanation, StringComparison.Ordinal);
        Assert.Contains(
            "1 because the check did not answer",
            coverage.Explanation,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 because an ancestor is a reparse point",
            coverage.Explanation,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 because a name cannot be queried without being reinterpreted",
            coverage.Explanation,
            StringComparison.Ordinal);

        // The name that the check could not query without reinterpreting it is also a name the source
        // naming rules already report, and both facts are kept: the naming finding says what is wrong
        // with the name, and the coverage summary says the destination of that name was not examined.
        var nameProblem = findings.Only(WindowsPreflightFindingCodes.TargetNameInvalidCharacter);
        Assert.Equal(Severity.Error, nameProblem.Severity);
        Assert.Equal("odd<.txt", nameProblem.SourceRelativePath);

        Assert.Equal(4, findings.Count);
    }

    [Fact]
    public void ACancelledRunIsReportedAsCancelledRatherThanAsACleanResult()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.File("reached.txt", 1),
            SourceEntry.File("never-reached.txt", 1));

        var probe = ExistingObjectInputs.CancelledProbeResult(
            ExistingObjectObservation.ObservedMissing(
                "reached.txt",
                ExistingObjectInputs.Mapped("reached.txt"),
                ScriptedFileSystemApi.ErrorFileNotFound,
                "Scripted absence."),
            ExistingObjectObservation.NotQueried(
                "never-reached.txt",
                ExistingObjectGapReason.Cancelled,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                "Scripted cancellation.",
                ExistingObjectInputs.Mapped("never-reached.txt")));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var cancelled = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingObjectCheckCancelled, cancelled.Code);
        Assert.Equal(Severity.Info, cancelled.Severity);

        // A location the run never reached is unknown, never absent, and never a collision.
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed));
    }

    [Fact]
    public void ACancelledRunIsNotReportedAsHavingFinishedWithGaps()
    {
        var source = ExistingObjectInputs.Source(SourceEntry.File("never-reached.txt", 1));

        var probe = ExistingObjectInputs.CancelledProbeResult(
            ExistingObjectObservation.NotQueried(
                "never-reached.txt",
                ExistingObjectGapReason.Cancelled,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                "Scripted cancellation.",
                ExistingObjectInputs.Mapped("never-reached.txt")));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Single(findings);
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
    }

    [Fact]
    public void AnOmittedCheckIsReportedAsNotPerformedRatherThanAsAnAllClear()
    {
        // The two-argument entry point carries no check, and that is what it must say. Silence here
        // would be indistinguishable from "no destination is occupied".
        var findings = new WindowsPreflightEvaluator().Evaluate(
            ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024)),
            ExistingObjectInputs.UsableTarget());

        var finding = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed, finding.Code);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.SourceRelativePath);
        Assert.Contains("not a report", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitlyNotPerformedCheckIsReportedTheSameWay()
    {
        var findings = ExistingObjectInputs.Source(SourceEntry.File("report.txt", 1024))
            .EvaluateWith(
                ExistingObjectInputs.UsableTarget(),
                TargetExistingObjectProbeResult.NotPerformed());

        var finding = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed, finding.Code);
    }

    [Fact]
    public void ASourceScanFailureIsStillReportedAlongsideTheDestinationFacts()
    {
        // The new facts add to the report; they never replace or soften what the source side already
        // established about its own incompleteness.
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked")],
            SourceEntry.File("report.txt", 1024));

        var probe = ExistingObjectInputs.ProbeResult(
            ExistingObjectObservation.Observed(
                "report.txt",
                ExistingObjectInputs.Mapped("report.txt"),
                TargetObjectPresence.File,
                "Scripted observation."));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));
    }

    [Fact]
    public void ABranchStoppedByAnAncestorReportsTheCauseOnceAndNothingPerDescendant()
    {
        // The target holds a file where the source has a directory. The clash belongs to that one
        // location; everything mapped below it follows from the same fact.
        var source = ExistingObjectInputs.Source(
            SourceEntry.Directory("photos"),
            SourceEntry.Directory(@"photos\2026"),
            SourceEntry.File(@"photos\2026\a.jpg", 1),
            SourceEntry.File(@"photos\2026\b.jpg", 1));

        var probe = ExistingObjectInputs.ProbeResult(
            ExistingObjectObservation.Observed(
                "photos",
                ExistingObjectInputs.Mapped("photos"),
                TargetObjectPresence.File,
                "Scripted observation."),
            BlockedByExistingObject("photos", @"photos\2026"),
            BlockedByExistingObject("photos", @"photos\2026\a.jpg"),
            BlockedByExistingObject("photos", @"photos\2026\b.jpg"));

        // Coverage is not a gap here: an object cannot exist below a file, and the conflict that says
        // so has already been reported. Counting the descendants as unchecked would bury it.
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, probe.Outcome);

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        var conflict = Assert.Single(findings);

        Assert.Equal(WindowsPreflightFindingCodes.TargetExistingTypeCollision, conflict.Code);
        Assert.Equal("photos", conflict.SourceRelativePath);
    }

    [Fact]
    public void ADescendantOfAnUnanswerableParentIsNeverReportedAsAbsent()
    {
        var source = ExistingObjectInputs.Source(
            SourceEntry.Directory("photos"),
            SourceEntry.File(@"photos\a.jpg", 1));

        var probe = ExistingObjectInputs.ProbeResult(
            ExistingObjectObservation.CheckFailed(
                "photos",
                ExistingObjectInputs.Mapped("photos"),
                ScriptedFileSystemApi.ErrorAccessDenied,
                "Scripted failure."),
            ExistingObjectObservation.NotQueried(
                @"photos\a.jpg",
                ExistingObjectGapReason.BlockedByFailedCheck,
                "photos",
                ScriptedFileSystemApi.ErrorAccessDenied,
                "Scripted blocking ancestor.",
                ExistingObjectInputs.Mapped(@"photos\a.jpg")));

        Assert.False(probe.HasKind(ExistingObjectObservationKind.ObservedMissing));
        Assert.False(probe.HasKind(ExistingObjectObservationKind.InferredMissing));

        var findings = source.EvaluateWith(ExistingObjectInputs.UsableTarget(), probe);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCheckFailed));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingObjectCoverageIncomplete));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TargetExistingFileCollision));
        Assert.Equal(2, findings.Count);
    }

    private static ExistingObjectObservation BlockedByExistingObject(
        string ancestorRelativePath,
        string relativePath)
        => ExistingObjectObservation.NotQueried(
            relativePath,
            ExistingObjectGapReason.BlockedByExistingObject,
            ancestorRelativePath,
            nativeErrorCode: null,
            "Scripted blocking ancestor.",
            ExistingObjectInputs.Mapped(relativePath));
}

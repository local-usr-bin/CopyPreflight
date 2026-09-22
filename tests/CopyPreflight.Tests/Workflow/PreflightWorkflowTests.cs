using System.IO;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The workflow over real folders: proof that it drives the real scanner, the real target probes and
/// the real evaluator, and that what those produce survives the orchestration.
/// </summary>
/// <remarks>
/// Everything here runs against directories and files this test owns inside the repository's own
/// fixture folder. Nothing is simulated unless a case says so, and no test needs a network share,
/// administrator rights or a change to any machine setting.
/// </remarks>
public class PreflightWorkflowTests
{
    [Fact]
    public async Task RunAsync_OverRealFolders_PerformsEveryStageAndFindsNoBlockingIssue()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("notes.txt", 12);
        source.CreateDirectory("sub");

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Completed, result.Status);
        Assert.Null(result.StoppedInPhase);
        Assert.False(result.HasCoverageGaps);

        // The source observation is the real one, and the resolved root is the root that was walked.
        var scan = Assert.IsType<SourceScanResult>(result.SourceScan);
        Assert.Equal(source.Root, scan.SourceRoot);
        Assert.Equal(1L, scan.FileCount);
        Assert.Equal(1L, scan.DirectoryCount);
        Assert.Contains(scan.Entries, entry => entry.RelativePath == "notes.txt" && entry.LengthInBytes == 12);

        // The target probe is the real one, and it answered about the target that was asked about.
        var probe = Assert.IsType<TargetProbeResult>(result.Target);
        Assert.Equal(target.Root, probe.TargetDirectoryPath);

        // The existing-object check ran over the real target and covered everything.
        var existingObjects = Assert.IsType<TargetExistingObjectProbeResult>(result.ExistingObjects);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, existingObjects.Outcome);

        // The evaluator was given those facts: the "no check was supplied" gap cannot appear here,
        // because no run of this workflow omits the check.
        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Code == WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed);

        Assert.DoesNotContain(result.Findings, finding => finding.Severity == Severity.Error);
    }

    [Fact]
    public async Task RunAsync_WhenAFileAlreadyOccupiesTheDestination_ReportsTheFileCollisionWarning()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("dup.txt", 4);
        target.CreateFile("dup.txt", 4);

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        var collision = result.Findings.Only(WindowsPreflightFindingCodes.TargetExistingFileCollision);

        Assert.Equal(Severity.Warning, collision.Severity);
        Assert.Equal("dup.txt", collision.SourceRelativePath);
        Assert.Equal(PreflightRunStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunAsync_WhenAKindClashesAtTheDestination_ReportsTheTypeCollisionError()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateDirectory("shared");
        target.CreateFile("shared", 1);

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        var collision = result.Findings.Only(WindowsPreflightFindingCodes.TargetExistingTypeCollision);

        Assert.Equal(Severity.Error, collision.Severity);
        Assert.Equal("shared", collision.SourceRelativePath);
    }

    [Fact]
    public async Task RunAsync_WhenAFileBlocksADirectory_ReportsItOnceAtTheBlockingObject()
    {
        // The M6 fix this exercises: an inventory names objects, not the directories they sit in, so
        // the structural conflict belongs to the object that causes it and must be reported once —
        // not once per descendant, and not lost because the blocked objects have no finding of their
        // own.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateDirectory("blocked");
        source.CreateFile(Path.Combine("blocked", "inside.txt"), 2);
        target.CreateFile("blocked", 1);

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        var blocked = Path.Combine("blocked", "inside.txt");

        var collision = result.Findings.Only(WindowsPreflightFindingCodes.TargetExistingTypeCollision);
        Assert.Equal("blocked", collision.SourceRelativePath);
        Assert.DoesNotContain(result.Findings, finding => finding.SourceRelativePath == blocked);

        // A branch stopped by an ordinary file is a decided conflict, not a coverage gap: everything
        // below it is accounted for, and the check still counts as covered.
        var existingObjects = Assert.IsType<TargetExistingObjectProbeResult>(result.ExistingObjects);
        Assert.Equal(TargetExistingObjectProbeOutcome.Completed, existingObjects.Outcome);
        Assert.False(result.HasCoverageGaps);
    }

    [Fact]
    public async Task RunAsync_WhenTheTargetIsNotADirectory_StillProducesTheTargetWideFinding()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("a.txt", 1);
        var targetIsAFile = target.CreateFile("not-a-directory.txt", 1);

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(source.Root, targetIsAFile));

        var blocker = result.Findings.Only(WindowsPreflightFindingCodes.TargetPathUnavailable);

        // A target-wide conclusion belongs to no single source object, and it must stay that way.
        Assert.Null(blocker.SourceRelativePath);
        Assert.Equal(Severity.Error, blocker.Severity);

        // The run still completed: the target's own rejection is a fact, not a crash, and it is still
        // reported together with whatever the source side had to say.
        Assert.Equal(PreflightRunStatus.Completed, result.Status);
        Assert.Contains(result.Target!.Issues, issue => issue.Kind == TargetProbeIssueKind.TargetPathUnavailable);

        // The check was supplied rather than omitted, so the "not performed" gap must not appear in
        // place of the facts.
        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Code == WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed);
    }

    [Fact]
    public async Task RunAsync_WhenPartOfTheSourceCannotBeListed_KeepsTheIssueAndTheCoverageGap()
    {
        // Producing a listing failure honestly needs a directory this process may not list, which
        // would mean changing access control on the test machine. The scripted seam the scanner
        // already exposes is used instead, so the run is real in every other respect.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var locked = Path.Combine(source.Root, "locked");
        var reader = new ScriptedDirectoryReader()
            .WithChildren(source.Root, Observations.DirectoryItem("locked"), Observations.FileItem("ok.txt", 3))
            .WithFailure(locked, new UnauthorizedAccessException("The test denies this listing."));

        var result = await WorkflowInputs.Over(new SourceScanner(reader))
            .RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Completed, result.Status);

        // The issue survives the orchestration, and so does the fact that the inventory is partial.
        var scan = Assert.IsType<SourceScanResult>(result.SourceScan);
        Assert.False(scan.IsComplete);
        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.SourceScan);
        Assert.True(result.HasCoverageGaps);

        var incomplete = result.Findings.Only(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed);
        Assert.Equal(Severity.Error, incomplete.Severity);
        Assert.Equal("locked", incomplete.SourceRelativePath);

        // The part of the inventory that could be read was still evaluated.
        Assert.Contains(scan.Entries, entry => entry.RelativePath == "ok.txt");
    }

    [Fact]
    public async Task RunAsync_WhenTheTargetCapabilitiesArePartlyUnknown_StillExaminesTheDestinations()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("keep.txt", 3);

        var api = new ScriptedFileSystemApi()
            .WithAttributes(FileAttributes.Directory)
            .WithVolumePathFailure(nativeError: 21)
            .WithAttributeFailureFor(
                WorkflowInputs.Mapped("keep.txt", target.Root),
                ScriptedFileSystemApi.ErrorFileNotFound);

        var result = await WorkflowInputs.OverScriptedTarget(api)
            .RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Completed, result.Status);

        // A capability that stayed unknown is a gap in what is known about the target, not a reason
        // to stop examining the destinations.
        Assert.True(result.HasCoverageGaps);
        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.TargetCapabilities);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.SourceScan);
        Assert.NotEmpty(api.AttributePaths);
        Assert.Contains(result.Findings, finding => finding.Code == WindowsPreflightFindingCodes.TargetFileSystemUnknown);
    }

    [Fact]
    public async Task RunAsync_ReportsProgressForEveryStageAndForTheSourceObservation()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("a.txt", 1);

        var progress = new RecordingRunProgress();
        var request = WorkflowInputs.Request(source.Root, target.Root);

        await new PreflightWorkflow().RunAsync(request, progress);

        Assert.NotEmpty(progress.Snapshots);

        // Every snapshot belongs to the run that produced it.
        Assert.All(progress.Snapshots, snapshot => Assert.Equal(request.RequestId, snapshot.RequestId));

        // Each stage announces itself, in order and without going backwards.
        var phases = progress.Snapshots.Select(snapshot => snapshot.Phase).ToList();
        Assert.Equal(phases.OrderBy(phase => phase), phases);
        Assert.Equal(PreflightRunPhase.ScanningSource, phases[0]);
        Assert.Contains(PreflightRunPhase.ProbingTargetCapabilities, phases);
        Assert.Contains(PreflightRunPhase.ProbingExistingObjects, phases);
        Assert.Contains(PreflightRunPhase.EvaluatingRules, phases);

        // The source counters are carried through the later stages instead of disappearing once the
        // source observation is over.
        Assert.All(
            progress.Snapshots.Where(snapshot => snapshot.Phase != PreflightRunPhase.ScanningSource),
            snapshot => Assert.NotNull(snapshot.SourceScan));
    }
}

using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The run result's own guarantees: what it refuses to represent, and how it describes coverage.
/// </summary>
/// <remarks>
/// Coverage is derived from the facts and outcomes a run holds, never from its findings. These tests
/// state that directly, because a report that inferred "everything was checked" from "no Error was
/// produced" is exactly the failure this type exists to prevent.
/// </remarks>
public class PreflightRunResultTests
{
    // ------------------------------------------------------------------ what a result refuses

    [Fact]
    public void Constructor_RejectsACompletedRunWithoutTheSourceInventory()
    {
        var source = PreflightInputs.Source();

        Assert.Throws<ArgumentException>(() => Completed(
            sourceScan: null,
            existingObjects: PreflightInputs.FoundNothingAtDestinations(source)));
    }

    [Fact]
    public void Constructor_RejectsACompletedRunWithoutTheTargetFacts()
    {
        var source = PreflightInputs.Source();

        Assert.Throws<ArgumentException>(() => Completed(
            target: null,
            existingObjects: PreflightInputs.FoundNothingAtDestinations(source)));
    }

    [Fact]
    public void Constructor_RejectsACompletedRunWithoutTheExistingObjectFacts()
    {
        // Completing the sequence without the destinations having been examined would mean reporting a
        // check that never happened.
        Assert.Throws<ArgumentException>(() => Completed(existingObjects: null));
    }

    [Fact]
    public void Constructor_RejectsACompletedRunThatStoppedInAStage()
    {
        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Completed,
            stoppedInPhase: PreflightRunPhase.EvaluatingRules,
            sourceScan: null,
            sourceObservedSoFar: null,
            target: null,
            existingObjects: null,
            findings: [],
            failure: null));
    }

    [Fact]
    public void Constructor_RejectsAnUnfinishedRunWithoutTheStageItStoppedIn()
    {
        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: null,
            sourceScan: null,
            sourceObservedSoFar: null,
            target: null,
            existingObjects: null,
            findings: [],
            failure: null));
    }

    [Fact]
    public void Constructor_RejectsAnUnfinishedRunCarryingFindings()
    {
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        // A partial evaluation next to a whole one would be indistinguishable from it, so an unfinished
        // run reports facts and gaps and no verdict at all.
        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.EvaluatingRules,
            source,
            sourceObservedSoFar: null,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source),
            findings:
            [
                new Finding(
                    WindowsPreflightFindingCodes.TargetExistingObjectCheckNotPerformed,
                    Severity.Info,
                    "A finding the test supplies",
                    "An unfinished run may not carry findings.",
                    "It is rejected rather than shown.",
                    "Nothing is recommended."),
            ],
            failure: null));
    }

    [Fact]
    public void Constructor_RejectsAFailedRunWithoutAFailure()
    {
        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Failed,
            stoppedInPhase: PreflightRunPhase.ScanningSource,
            sourceScan: null,
            sourceObservedSoFar: null,
            target: null,
            existingObjects: null,
            findings: [],
            failure: null));
    }

    [Fact]
    public void Constructor_RejectsACancelledRunCarryingAFailure()
    {
        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.ScanningSource,
            sourceScan: null,
            sourceObservedSoFar: null,
            target: null,
            existingObjects: null,
            findings: [],
            failure: new PreflightRunFailure(
                PreflightRunPhase.ScanningSource,
                "System.InvalidOperationException",
                "Only a failed run carries a failure.")));
    }

    [Fact]
    public void Constructor_RejectsADestinationCheckWithoutATarget()
    {
        var source = PreflightInputs.Source();

        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.ProbingExistingObjects,
            source,
            sourceObservedSoFar: null,
            target: null,
            PreflightInputs.FoundNothingAtDestinations(source),
            findings: [],
            failure: null));
    }

    [Fact]
    public void Constructor_RejectsAPartialObservationNextToAReturnedInventory()
    {
        var source = PreflightInputs.Source();

        Assert.Throws<ArgumentException>(() => new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.ProbingTargetCapabilities,
            source,
            sourceObservedSoFar: new SourceScanProgress(1, 1, 0, 0, 1, 0, null),
            target: null,
            existingObjects: null,
            findings: [],
            failure: null));
    }

    // ------------------------------------------------------------------ coverage

    [Fact]
    public void Coverage_OfAWholeCompletedRun_IsCompleteEverywhere()
    {
        var result = CleanCompleted();

        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.SourceScan);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.TargetCapabilities);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.ExistingObjects);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.Rules);
        Assert.False(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfAPartlyObservedSource_IsIncompleteEvenWithNoFindings()
    {
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked")],
            SourceEntry.File("a.txt", 1));

        var result = Completed(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.SourceScan);
        Assert.True(result.HasCoverageGaps);

        // The gap comes from the issue the scan recorded, not from anything the findings happen to say.
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Coverage_OfAPartlyKnownTarget_IsIncompleteForEachCapability(
        bool unknownFileSystem,
        bool unknownFreeSpace,
        bool unknownComponentLimit)
    {
        var target = PreflightInputs.Target(
            unknownFileSystem
                ? CapabilityValue<FileSystemName>.Unknown("The test leaves this unknown.")
                : CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            unknownFreeSpace
                ? CapabilityValue<long>.Unknown("The test leaves this unknown.")
                : CapabilityValue<long>.Known(long.MaxValue),
            unknownComponentLimit
                ? CapabilityValue<int>.Unknown("The test leaves this unknown.")
                : CapabilityValue<int>.Known(255));

        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var result = Completed(source, target, PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.TargetCapabilities);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfADestinationCheckWithGaps_IsIncomplete()
    {
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var existingObjects = ExistingObjectInputs.ProbeResult(
            ExistingObjectObservation.NotQueried(
                "a.txt",
                ExistingObjectGapReason.AmbiguousName,
                blockingAncestorRelativePath: null,
                nativeErrorCode: null,
                "The test leaves this location unexamined."));

        var result = Completed(source, PreflightInputs.HealthyTarget(), existingObjects);

        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.ExistingObjects);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfACheckThatWasNeverCarriedOut_IsNotPerformed()
    {
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var result = Completed(
            source,
            PreflightInputs.HealthyTarget(),
            TargetExistingObjectProbeResult.NotPerformed());

        // "No check was carried out" is not the same claim as "the check ran and covered nothing", and
        // it is certainly not the same as "no object occupies the destinations".
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.ExistingObjects);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfACancelledDestinationCheck_IsIncomplete()
    {
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var result = Completed(
            source,
            PreflightInputs.HealthyTarget(),
            ExistingObjectInputs.CancelledProbeResult(
                ExistingObjectObservation.NotQueried(
                    "a.txt",
                    ExistingObjectGapReason.Cancelled,
                    blockingAncestorRelativePath: null,
                    nativeErrorCode: null,
                    "The test stopped the check here.")));

        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.ExistingObjects);
    }

    [Fact]
    public void Coverage_OfAnUnfinishedRun_KeepsWhatReturnedAndNothingElse()
    {
        var result = new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.ProbingTargetCapabilities,
            sourceScan: PreflightInputs.Source(SourceEntry.File("a.txt", 1)),
            sourceObservedSoFar: null,
            target: null,
            existingObjects: null,
            findings: [],
            failure: null);

        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.SourceScan);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.TargetCapabilities);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.ExistingObjects);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.Rules);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfAnObservationThatStartedAndDidNotReturn_IsIncompleteNotNotPerformed()
    {
        // The two are different claims. "The source was never looked at" and "the source was looked at
        // over part of the tree and the look was stopped" lead to different next steps, and the partial
        // counters carried beside this coverage would contradict the first one.
        var result = new PreflightRunResult(
            WorkflowInputs.Request(),
            PreflightRunStatus.Cancelled,
            stoppedInPhase: PreflightRunPhase.ScanningSource,
            sourceScan: null,
            sourceObservedSoFar: new SourceScanProgress(600, 600, 0, 0, 600, 0, "a.txt"),
            target: null,
            existingObjects: null,
            findings: [],
            failure: null);

        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.SourceScan);
        Assert.Equal(PreflightRunStatus.Cancelled, result.Status);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public void Coverage_OfABlockedTarget_ShowsTheTargetDependentStagesAsUnestablished()
    {
        // A target the probe rejected is still a result, and the stages that depend on it are gaps
        // rather than silently missing.
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var result = Completed(
            source,
            PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.SourceScan);
        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.TargetCapabilities);
        Assert.True(result.HasCoverageGaps);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A completed run over a one-file source, a healthy target and destinations that were checked.</summary>
    private static PreflightRunResult CleanCompleted()
    {
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        return Completed(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));
    }

    /// <summary>
    /// Builds the facts of a completed run. The facts are passed in verbatim — including a
    /// <see langword="null"/> one, which is the point of several of the tests above.
    /// </summary>
    private static PreflightRunResult Completed(
        SourceScanResult? sourceScan = null,
        TargetProbeResult? target = null,
        TargetExistingObjectProbeResult? existingObjects = null)
        => new(
            WorkflowInputs.Request(),
            PreflightRunStatus.Completed,
            stoppedInPhase: null,
            sourceScan,
            sourceObservedSoFar: null,
            target,
            existingObjects,
            findings: [],
            failure: null);
}

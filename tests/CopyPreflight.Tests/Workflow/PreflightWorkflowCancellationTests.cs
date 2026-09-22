using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// What a run does when it is cancelled, and what it does when something breaks.
/// </summary>
/// <remarks>
/// A cancelled run and a failed run are different things, and neither of them is an assessment. These
/// tests pin down both: what is kept, what stays unestablished, and what must never be reported.
/// </remarks>
public class PreflightWorkflowCancellationTests
{
    [Fact]
    public async Task RunAsync_WhenCancelledBeforeItStarts_ReturnsCancelledAndObservesNothing()
    {
        using var source = new TemporarySourceTree();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var reader = new ScriptedDirectoryReader()
            .WithChildren(source.Root, Observations.FileItem("a.txt", 1));

        var result = await WorkflowInputs.Over(new SourceScanner(reader)).RunAsync(
            WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot),
            progress: null,
            cancellation.Token);

        Assert.Equal(PreflightRunStatus.Cancelled, result.Status);
        Assert.Equal(PreflightRunPhase.ScanningSource, result.StoppedInPhase);
        Assert.Empty(reader.ListedDirectories);

        Assert.Null(result.SourceScan);
        Assert.Null(result.SourceObservedSoFar);
        Assert.Null(result.Target);
        Assert.Null(result.ExistingObjects);
        Assert.Empty(result.Findings);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.SourceScan);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringTheSourceObservation_KeepsTheCountersItHadObserved()
    {
        const int CancelAfterChildren = 600;
        const int MaximumChildren = 10_000;

        using var source = new TemporarySourceTree();
        using var cancellation = new CancellationTokenSource();

        var yielded = 0;

        IEnumerable<ObservedDirectoryChild> Children()
        {
            for (var index = 0; index < MaximumChildren; index++)
            {
                yielded++;

                if (yielded == CancelAfterChildren)
                {
                    cancellation.Cancel();
                }

                yield return Observations.FileItem($"file{index:D5}.bin", 1L);
            }
        }

        var reader = new ScriptedDirectoryReader().WithListing(source.Root, Children);

        var result = await WorkflowInputs.Over(new SourceScanner(reader)).RunAsync(
            WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot),
            progress: null,
            cancellation.Token);

        Assert.Equal(PreflightRunStatus.Cancelled, result.Status);
        Assert.Equal(PreflightRunPhase.ScanningSource, result.StoppedInPhase);

        // Nothing that did not return is invented: there is no inventory, and no target fact.
        Assert.Null(result.SourceScan);
        Assert.Null(result.Target);
        Assert.Null(result.ExistingObjects);
        Assert.Empty(result.Findings);

        // The observation was carried out over part of the tree and then stopped, so it is incomplete
        // rather than never performed — the gap and the counters that were seen agree with each other.
        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.SourceScan);

        // What the observation had reported is kept, and it says exactly what it is: observed so far.
        var observed = Assert.IsType<SourceScanProgress>(result.SourceObservedSoFar);
        Assert.True(observed.EntriesObserved > 0);
        Assert.Equal(observed.EntriesObserved, observed.FilesObserved);
        Assert.Equal(0L, observed.DirectoriesObserved);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledBetweenStages_DoesNotStartTheNextStage()
    {
        using var source = new TemporarySourceTree();
        using var cancellation = new CancellationTokenSource();

        source.CreateFile("a.txt", 1);

        var api = new ScriptedFileSystemApi().WithAttributes(FileAttributes.Directory);

        // The last snapshot of the source observation arrives when the source tree has been walked, so
        // cancelling from there models a user pressing Cancel just as the first stage ends.
        var progress = new RecordingRunProgress(snapshot =>
        {
            if (snapshot.Phase == PreflightRunPhase.ScanningSource && snapshot.SourceScan is not null)
            {
                cancellation.Cancel();
            }
        });

        var result = await WorkflowInputs.OverScriptedTarget(api)
            .RunAsync(WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot), progress, cancellation.Token);

        Assert.Equal(PreflightRunStatus.Cancelled, result.Status);

        // The source observation had finished, so its inventory is real and is kept.
        Assert.NotNull(result.SourceScan);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.SourceScan);

        // The target was never probed: cancelling did not start another stage.
        Assert.Null(result.Target);
        Assert.Null(result.ExistingObjects);
        Assert.Empty(api.AttributePaths);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.TargetCapabilities);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringTheExistingObjectCheck_StopsWithoutAssessing()
    {
        using var source = new TemporarySourceTree();
        using var cancellation = new CancellationTokenSource();

        // Two entries, so the check has another one to reach after the cancellation is noticed.
        source.CreateFile("a.txt", 1);
        source.CreateFile("b.txt", 1);

        var api = new ScriptedFileSystemApi()
            .WithAttributes(FileAttributes.Directory)
            .WithCancellationAfter(attributeQueries: 2, cancellation);

        var result = await WorkflowInputs.OverScriptedTarget(api).RunAsync(
            WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot),
            progress: null,
            cancellation.Token);

        Assert.Equal(PreflightRunStatus.Cancelled, result.Status);
        Assert.Equal(PreflightRunPhase.ProbingExistingObjects, result.StoppedInPhase);

        // The stages that returned are kept; the check that was interrupted returns nothing at all, so
        // its partial observations stay unestablished rather than being presented as facts.
        Assert.NotNull(result.SourceScan);
        Assert.NotNull(result.Target);
        Assert.Null(result.ExistingObjects);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.ExistingObjects);

        // And nothing was derived from the partial facts.
        Assert.Empty(result.Findings);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.Rules);
    }

    [Fact]
    public async Task RunAsync_WhenAStageBreaks_ReportsFailedInsteadOfAnEmptyResult()
    {
        using var source = new TemporarySourceTree();

        var reader = new FlakyDirectoryReader(
            new InvalidOperationException("The test made the first listing fail."));

        var result = await WorkflowInputs.Over(new SourceScanner(reader)).RunAsync(
            WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot));

        Assert.Equal(PreflightRunStatus.Failed, result.Status);
        Assert.Equal(PreflightRunPhase.ScanningSource, result.StoppedInPhase);

        var failure = Assert.IsType<PreflightRunFailure>(result.Failure);
        Assert.Equal("System.InvalidOperationException", failure.ExceptionTypeName);
        Assert.Equal(PreflightRunPhase.ScanningSource, failure.Phase);

        // A failure is never dressed up as a clean result.
        Assert.Empty(result.Findings);
        Assert.Null(result.SourceScan);
        Assert.Equal(PreflightPhaseCoverage.NotPerformed, result.Coverage.SourceScan);
        Assert.True(result.HasCoverageGaps);
    }

    [Fact]
    public async Task RunAsync_WhenACancellationBelongsToAnotherToken_TreatsItAsAFailure()
    {
        using var source = new TemporarySourceTree();
        using var unrelated = new CancellationTokenSource();

        var reader = new FlakyDirectoryReader(
            new OperationCanceledException("Not this run's cancellation.", unrelated.Token));

        var result = await WorkflowInputs.Over(new SourceScanner(reader)).RunAsync(
            WorkflowInputs.Request(source.Root, WorkflowInputs.TargetRoot));

        // An unrelated cancellation must not be presented as the user's own request to stop.
        Assert.Equal(PreflightRunStatus.Failed, result.Status);
        Assert.Equal(
            "System.OperationCanceledException",
            Assert.IsType<PreflightRunFailure>(result.Failure).ExceptionTypeName);
    }

    [Fact]
    public async Task RunAsync_AfterAFailure_CanRunAgainAndComplete()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new FlakyDirectoryReader(
            new InvalidOperationException("The test made the first listing fail."));

        var workflow = WorkflowInputs.Over(new SourceScanner(reader));

        var failed = await workflow.RunAsync(WorkflowInputs.Request(source.Root, target.Root));
        var second = await workflow.RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Failed, failed.Status);

        // The workflow carries no state from the failed run: a new run gets a new token source, a new
        // request and a clean slate, and it is not blocked by what happened before.
        Assert.Equal(PreflightRunStatus.Completed, second.Status);
        Assert.NotEqual(failed.Request.RequestId, second.Request.RequestId);
        Assert.True(reader.ListingsRequested >= 2, "The second run should have observed the source again.");
    }
}

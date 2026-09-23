using System.IO;
using CopyPreflight.App;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The presentation state machine, exercised without a window.
/// </summary>
/// <remarks>
/// <para>
/// The window only binds to this type and forwards events, so everything that decides what the screen
/// shows is asserted here: which run a snapshot belongs to, what happens to a result when the input
/// changes, what a cancelled run may say, and whether a close is refused while work is in progress.
/// </para>
/// <para>
/// Where a run has to be held in a particular state, a directory reader that blocks until the test
/// releases it is used. Nothing here sleeps and waits for something to happen.
/// </para>
/// </remarks>
public class CheckViewModelTests
{
    // ------------------------------------------------------------------ the initial screen

    [Fact]
    public void BeforeAnyRun_TheScreenShowsUncheckedValuesRatherThanZeroes()
    {
        var viewModel = new CheckViewModel();

        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.Equal("Ready. Nothing has been checked yet.", viewModel.StateText);
        Assert.True(viewModel.CanStartCheck);
        Assert.False(viewModel.CanCancelCheck);
        Assert.True(viewModel.IsInputEnabled);

        // A count that was never observed is not a zero, and a capability that was never asked about is
        // not a value.
        Assert.Contains("not observed", viewModel.FilesText);
        Assert.Contains("Not checked", viewModel.FileSystemText);
        Assert.Contains("not performed", viewModel.SourceCoverageText);

        // An empty findings list here does not mean "nothing was found".
        Assert.False(viewModel.HasFindings);
        Assert.Equal("Nothing has been checked yet.", viewModel.FindingsEmptyText);
        Assert.Equal("Nothing has been checked yet.", viewModel.ConclusionText);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Empty(viewModel.BlockingStatementText);
    }

    // ------------------------------------------------------------------ input handling

    [Fact]
    public async Task StartCheckAsync_WithAnInputItWillNotAccept_RunsNothingAndSaysWhy()
    {
        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = "relative-folder";
        viewModel.TargetPath = @"C:\target";

        await viewModel.StartCheckAsync();

        Assert.True(viewModel.HasInputMessage);
        Assert.Contains("complete path", viewModel.InputMessage);
        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.Empty(viewModel.Findings);

        // The refusal happened before anything was observed.
        Assert.Empty(reader.ListedDirectories);
        reader.Release();
    }

    [Fact]
    public async Task StartCheckAsync_WithoutASourceFolder_SaysWhichBoxIsEmpty()
    {
        var viewModel = new CheckViewModel();
        viewModel.TargetPath = @"C:\target";

        await viewModel.StartCheckAsync();

        Assert.Equal("Enter the source folder to check.", viewModel.InputMessage);
        Assert.Equal(CheckState.Idle, viewModel.State);
    }

    // ------------------------------------------------------------------ one run at a time

    [Fact]
    public async Task StartCheckAsync_WhenStartedTwice_StartsOnlyOneRun()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var first = viewModel.StartCheckAsync();

        Assert.True(reader.WaitUntilListingStarted(), "The first run should have begun observing the source.");
        Assert.Equal(CheckState.Running, viewModel.State);
        Assert.False(viewModel.CanStartCheck);
        Assert.False(viewModel.IsInputEnabled);

        // The second press finds a busy screen and does nothing at all.
        var second = viewModel.StartCheckAsync();
        Assert.True(second.IsCompleted, "A second start must return without starting another run.");

        reader.Release();
        await first;
        await second;

        Assert.Single(reader.ListedDirectories);
        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.True(viewModel.CanStartCheck);
    }

    [Fact]
    public async Task CancelCheck_WhileRunning_ShowsCancellingImmediatelyAndStaysBusy()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());

        viewModel.CancelCheck();

        // The request is shown at once, but the work has not stopped, so the screen stays busy and a
        // second check still cannot be started on top of it.
        Assert.Equal(CheckState.Cancelling, viewModel.State);
        Assert.Contains("Cancellation requested", viewModel.StateText);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanStartCheck);
        Assert.False(viewModel.CanCancelCheck);

        reader.Release();
        await run;

        Assert.Equal(CheckState.Cancelled, viewModel.State);
        Assert.True(viewModel.CanStartCheck);
    }

    [Fact]
    public async Task CancelCheck_WhenNothingIsRunning_DoesNotRewriteAFinishedRun()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var viewModel = CompletingViewModel(source.Root, target.Root);

        await viewModel.StartCheckAsync();
        Assert.Equal(CheckState.Completed, viewModel.State);

        viewModel.CancelCheck();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("Assessment finished.", viewModel.StateText);
    }

    /// <summary>
    /// Cancels in the window between the run finishing and its result reaching the screen.
    /// </summary>
    /// <remarks>
    /// This is the timing the other cancellation tests do not reach: the check is over — every stage
    /// ran and its findings were derived — but the screen has not been told yet. The cancellation is
    /// real (the run's own token is cancelled and the screen says so), and the result that follows is
    /// the one that finished. Publishing "cancelled" here would state that a check which ran every
    /// stage had not run, and would drop its findings from the screen.
    /// </remarks>
    [Fact]
    public async Task CancelCheck_AfterTheRunFinishedButBeforeItsResultIsShown_StillShowsTheRunThatFinished()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("notes.txt", lengthInBytes: 16);

        // The production sink, so the delivery path under test is the real one: it captures the
        // context that is current when the run starts, and that is this thread's.
        var viewModel = new CheckViewModel(WorkflowInputs.Over());
        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var interfaceThread = new ManualSynchronizationContext();
        var outer = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(interfaceThread);

        Task run;

        try
        {
            run = viewModel.StartCheckAsync();

            // Step 1: the run has begun and the screen is busy.
            Assert.Equal(CheckState.Running, viewModel.State);
            Assert.True(viewModel.IsBusy);

            // Nothing runs this queue on its own, so the screen cannot have been told anything yet:
            // the counts still say they were never observed.
            Assert.Contains("not observed", viewModel.FilesText);

            // Step 2: the workflow has already returned its result. While it runs, coalescing keeps
            // the queue at the single callback that carries its snapshots; the continuation is queued
            // behind it only once the run's task completes. So two waiting callbacks means the result
            // is in hand — and that no callback has run to get there.
            Assert.True(
                interfaceThread.WaitUntilPendingCountAtLeast(2, TimeSpan.FromMinutes(1)),
                "The workflow should have returned its result already.");
            Assert.Equal(2, interfaceThread.PendingCount);

            // Step 3 and 4: the result is not shown yet, and the cancellation asked for here is real.
            viewModel.CancelCheck();

            Assert.Equal(CheckState.Cancelling, viewModel.State);
            Assert.Contains("Cancellation requested", viewModel.StateText);
            Assert.True(viewModel.IsBusy, "The screen stays busy until the run has genuinely ended.");
            Assert.False(viewModel.CanStartCheck);

            // Step 5: now the result reaches the screen.
            interfaceThread.Drain();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(outer);
        }

        await run;

        // Step 6: the check that finished is the check that is shown. Every stage ran — the phase
        // line says so, and the counts are the real ones rather than a rewritten "not completed".
        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("Assessment finished.", viewModel.StateText);
        Assert.Equal("All stages performed.", viewModel.PhaseText);
        Assert.Contains("Regular files: 1", viewModel.FilesText);
        Assert.True(viewModel.HasBlockingStatement);

        // Step 3 of the other shape, kept: what has genuinely been published is not rewritten by a
        // later cancellation.
        viewModel.CancelCheck();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("All stages performed.", viewModel.PhaseText);
    }

    [Fact]
    public async Task StartCheckAsync_AfterACancelledRun_CanRunAgainAndFinish()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var cancelledRun = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());
        viewModel.CancelCheck();
        reader.Release();
        await cancelledRun;

        Assert.Equal(CheckState.Cancelled, viewModel.State);

        // A new run gets a new token source and is not held back by the cancelled one.
        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal(2, reader.ListedDirectories.Count);
    }

    // ------------------------------------------------------------------ what the screen concludes

    [Fact]
    public async Task StartCheckAsync_WhenTheCheckFinishesWithNoBlockingIssue_ShowsTheFrozenConclusion()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var viewModel = CompletingViewModel(source.Root, target.Root);

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("Assessment finished.", viewModel.StateText);
        Assert.True(viewModel.HasBlockingStatement);
        Assert.Equal("No known blocking issue was detected.", viewModel.BlockingStatementText);
        Assert.False(viewModel.HasFindings);
        Assert.DoesNotContain("coverage gaps", viewModel.ConclusionText);
    }

    [Fact]
    public async Task StartCheckAsync_WhenABlockingIssueIsFound_ShowsItAndWithholdsTheFrozenStatement()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        // A source directory whose destination already holds an ordinary file is a decided conflict.
        var api = new ScriptedFileSystemApi()
            .WithAttributes(FileAttributes.Directory)
            .WithAttribute(WorkflowInputs.Mapped("shared", target.Root), FileAttributes.Archive);

        var viewModel = CompletingViewModel(source.Root, target.Root, api, Observations.DirectoryItem("shared"));

        await viewModel.StartCheckAsync();

        // An Error finding is not a failure of the application: the run completed and reported it.
        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.True(viewModel.HasFindings);
        Assert.Equal("Error", viewModel.Findings[0].SeverityLabel);
        Assert.Equal("shared", viewModel.Findings[0].SourceRelativePath);
        Assert.Contains("blocking issue", viewModel.ConclusionText);

        Assert.False(viewModel.HasBlockingStatement);
        Assert.Empty(viewModel.BlockingStatementText);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheSourceRootIsUnusable_DoesNotClaimEveryStageWasPerformed()
    {
        // The run finishes, but the existing-object stage never happened: a root that could not be used
        // leaves no inventory to check the destinations against. A finished run must not be presented as
        // one that carried out every stage, and the stage that did not run must not read as complete.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var viewModel = WorkflowInputs.ViewModelOver(new PreflightWorkflow());
        viewModel.SourcePath = source.PathOf("no-inventory-here");
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("Assessment finished.", viewModel.StateText);
        Assert.Equal(PreflightText.NotEveryStagePerformed, viewModel.PhaseText);

        // The stage says what it did and why, so the absent inventory is visible on the screen rather
        // than only in the finding list.
        Assert.Contains("Existing-object check: not performed", viewModel.ExistingObjectCoverageText);
        Assert.Contains("no source inventory", viewModel.ExistingObjectCoverageText);
        Assert.DoesNotContain("complete", viewModel.ExistingObjectCoverageText);

        // The blocking conclusion is the source root's, and the frozen statement stays withheld.
        Assert.Contains("blocking issue", viewModel.ConclusionText);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Equal(
            Severity.Error,
            viewModel.Findings
                .Single(finding => finding.Code == WindowsPreflightFindingCodes.SourceRootUnusable.Value)
                .Severity);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheSourceIsAnEmptyDirectory_ShowsEveryStagePerformed()
    {
        // The contrast that has to survive: a source tree that really is empty is a known inventory, so
        // the destinations were checked against it and the run did carry out every stage. Zero entries
        // because nothing was there is not the same fact as zero entries because nobody looked.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var viewModel = WorkflowInputs.ViewModelOver(new PreflightWorkflow());
        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal(PreflightText.AllStagesPerformed, viewModel.PhaseText);
        Assert.Equal("Existing-object check: complete", viewModel.ExistingObjectCoverageText);
        Assert.True(viewModel.HasBlockingStatement);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheCheckLeftGaps_SaysSoEvenWithNoBlockingIssue()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        // A target whose volume Windows will not describe: the capabilities stay unknown, which is a
        // gap in what the check covered rather than a problem with the migration.
        var viewModel = CompletingViewModel(
            source.Root,
            target.Root,
            new ScriptedFileSystemApi()
                .WithAttributes(FileAttributes.Directory)
                .WithVolumePathFailure(nativeError: 21));

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Equal("Assessment ended with coverage gaps.", viewModel.ConclusionText);
        Assert.Contains("incomplete", viewModel.TargetCoverageText);
        Assert.Contains("complete", viewModel.SourceCoverageText);

        // There is no blocking issue, so the frozen statement is accurate — but it is shown next to the
        // gap rather than instead of it.
        Assert.True(viewModel.HasBlockingStatement);
        Assert.Contains("coverage", viewModel.FindingsEmptyText);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheRunIsCancelled_ShowsNoConclusionAtAll()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        source.CreateFile("a.txt", 1);

        CheckViewModel? viewModel = null;

        // The last snapshot of the source observation is exactly where a user pressing Cancel just as
        // the first stage ends would be seen, because the Cancel button can only be pressed while the
        // screen is running. So that is where this test presses it.
        viewModel = new CheckViewModel(
            WorkflowInputs.Over(),
            handler => new SynchronousProgressSink<PreflightRunProgress>(snapshot =>
            {
                if (snapshot.Phase == PreflightRunPhase.ScanningSource && snapshot.SourceScan is not null)
                {
                    viewModel!.CancelCheck();
                }

                handler(snapshot);
            }));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Cancelled, viewModel.State);
        Assert.Contains("No assessment was produced.", viewModel.ConclusionText);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Empty(viewModel.BlockingStatementText);
        Assert.Empty(viewModel.Findings);
        Assert.Contains("derived only from a check that finishes", viewModel.FindingsEmptyText);

        // The source observation had returned before the cancellation was noticed, so its counts are
        // the finished observation they are — not dressed up as a partial one.
        Assert.Contains("complete", viewModel.SourceCoverageText);
        Assert.DoesNotContain("observed so far", viewModel.FilesText);
        Assert.Contains("derived only from a check that finishes", viewModel.FindingsEmptyText);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheSourceObservationIsInterrupted_KeepsThePartialCounts()
    {
        const int CancelAtChild = 600;

        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        CheckViewModel? viewModel = null;
        var yielded = 0;

        IEnumerable<ObservedDirectoryChild> Children()
        {
            for (var index = 0; index < 10_000; index++)
            {
                yielded++;

                if (yielded == CancelAtChild)
                {
                    // The same thing the Cancel button does, at the moment a check is in the middle of
                    // a very large directory.
                    viewModel!.CancelCheck();
                }

                yield return Observations.FileItem($"file{index:D5}.bin", 1L);
            }
        }

        var reader = new ScriptedDirectoryReader().WithListing(source.Root, Children);

        viewModel = WorkflowInputs.ViewModelOver(
            WorkflowInputs.OverScriptedTarget(
                new ScriptedFileSystemApi().WithAttributes(FileAttributes.Directory),
                new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Cancelled, viewModel.State);

        // What was observed is kept and says exactly what it is: a partial count, and not an inventory.
        Assert.Contains("observed so far", viewModel.FilesText);
        Assert.Contains("incomplete", viewModel.SourceCoverageText);
        Assert.Contains("not an inventory", viewModel.CountsNote);
        Assert.Equal("Target capabilities: not performed", viewModel.TargetCoverageText);
    }

    [Fact]
    public async Task StartCheckAsync_WhenTheRunFails_ShowsTheFailureAndNoConclusion()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new FlakyDirectoryReader(
            new InvalidOperationException("The test made the first listing fail."));

        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));
        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        Assert.Equal(CheckState.Failed, viewModel.State);
        Assert.True(viewModel.HasFailure);
        Assert.Contains("System.InvalidOperationException", viewModel.FailureText);
        Assert.Contains("No assessment was produced.", viewModel.ConclusionText);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Empty(viewModel.Findings);
    }

    // ------------------------------------------------------------------ stale input and stale callbacks

    [Fact]
    public async Task SourcePath_WhenChangedAfterACompletedRun_DiscardsTheResult()
    {
        using var source = new TemporarySourceTree();
        using var other = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var api = new ScriptedFileSystemApi()
            .WithAttributes(FileAttributes.Directory)
            .WithAttribute(WorkflowInputs.Mapped("shared", target.Root), FileAttributes.Archive);

        var viewModel = CompletingViewModel(source.Root, target.Root, api, Observations.DirectoryItem("shared"));

        await viewModel.StartCheckAsync();
        Assert.True(viewModel.HasFindings);

        viewModel.SourcePath = other.Root;

        // The finished assessment belonged to the previous folder, so it is gone rather than left
        // standing under a path it was not made for.
        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.Empty(viewModel.Findings);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Equal("Nothing has been checked yet.", viewModel.ConclusionText);
        Assert.Contains("not observed", viewModel.FilesText);
    }

    [Fact]
    public async Task TargetPath_WhenChangedAfterACompletedRun_DiscardsTheResult()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();
        using var other = new TemporarySourceTree();

        var viewModel = CompletingViewModel(source.Root, target.Root);

        await viewModel.StartCheckAsync();
        Assert.Equal(CheckState.Completed, viewModel.State);

        viewModel.TargetPath = other.Root;

        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.False(viewModel.HasBlockingStatement);
    }

    [Fact]
    public async Task Result_IsNeverShownUnderPathsItWasNotMadeFor()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();
        using var other = new TemporarySourceTree();

        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());

        // A change that disabling the boxes should prevent, but which the guarantee must not depend on.
        viewModel.SourcePath = other.Root;

        reader.Release();
        await run;

        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.Empty(viewModel.Findings);
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Contains(other.Root, viewModel.SourcePathText);
    }

    [Fact]
    public async Task Summary_WhenAPathWasResolved_ShowsBothTheResolvedPathAndWhatWasEntered()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var viewModel = CompletingViewModel(source.Root, target.Root);

        // A path with a redundant component: it leads to the same folder, but it is not the same text.
        viewModel.SourcePath = Path.Combine(source.Root, ".");

        await viewModel.StartCheckAsync();

        Assert.Contains(source.Root, viewModel.SourcePathText);
        Assert.Contains("entered as", viewModel.SourcePathText);
    }

    [Fact]
    public void ApplyProgress_FromAnotherRun_IsIgnored()
    {
        var viewModel = ViewModelRecordingProgress([]);

        viewModel.ApplyProgress(new PreflightRunProgress(Guid.NewGuid(), PreflightRunPhase.EvaluatingRules, null));

        Assert.Equal(CheckState.Idle, viewModel.State);
        Assert.Empty(viewModel.PhaseText);
    }

    [Fact]
    public async Task ApplyProgress_AfterTheRunHasEnded_DoesNotPutTheScreenBackToRunning()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var observed = new List<PreflightRunProgress>();
        var reader = new ControllableDirectoryReader();
        var viewModel = ViewModelRecordingProgress(observed, WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());
        viewModel.CancelCheck();
        reader.Release();
        await run;

        Assert.Equal(CheckState.Cancelled, viewModel.State);

        var phaseText = viewModel.PhaseText;

        // A snapshot that arrives after the run has ended is not news about anything on screen.
        viewModel.ApplyProgress(new PreflightRunProgress(
            observed[0].RequestId,
            PreflightRunPhase.ProbingExistingObjects,
            new SourceScanProgress(99, 99, 0, 0, 99, 0, null)));

        Assert.Equal(CheckState.Cancelled, viewModel.State);
        Assert.Equal(phaseText, viewModel.PhaseText);
    }

    [Fact]
    public async Task ApplyProgress_ThatWalksAStageBackwards_IsIgnored()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var observed = new List<PreflightRunProgress>();
        var reader = new ControllableDirectoryReader();
        var viewModel = ViewModelRecordingProgress(observed, WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());

        var requestId = observed[0].RequestId;

        viewModel.ApplyProgress(new PreflightRunProgress(requestId, PreflightRunPhase.EvaluatingRules, null));
        var advanced = viewModel.PhaseText;

        viewModel.ApplyProgress(new PreflightRunProgress(requestId, PreflightRunPhase.ScanningSource, null));

        Assert.Equal(advanced, viewModel.PhaseText);

        reader.Release();
        await run;
    }

    [Fact]
    public async Task ApplyProgress_FromTheRunningRequest_UpdatesTheStageAndThePartialCounts()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var observed = new List<PreflightRunProgress>();
        var reader = new ControllableDirectoryReader();
        var viewModel = ViewModelRecordingProgress(observed, WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());

        viewModel.ApplyProgress(new PreflightRunProgress(
            observed[0].RequestId,
            PreflightRunPhase.ProbingExistingObjects,
            new SourceScanProgress(7, 5, 1, 1, 42, 0, "sub")));

        Assert.Equal("Checking what already occupies the destinations", viewModel.PhaseText);
        Assert.Contains("observed so far", viewModel.FilesText);
        Assert.Contains("42", viewModel.LogicalBytesText);

        reader.Release();
        await run;
    }

    // ------------------------------------------------------------------ closing the window

    [Fact]
    public void TryBlockClose_WhenNoCheckIsRunning_AllowsTheClose()
    {
        Assert.False(new CheckViewModel().TryBlockClose());
    }

    [Fact]
    public async Task TryBlockClose_WhileACheckIsRunning_RefusesTheCloseAndAsksItToStop()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new ControllableDirectoryReader();
        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over(new SourceScanner(reader)));

        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var run = viewModel.StartCheckAsync();
        Assert.True(reader.WaitUntilListingStarted());

        Assert.True(viewModel.TryBlockClose(), "A close must be refused while work is still going on.");
        Assert.Equal(CheckState.Cancelling, viewModel.State);

        // The window is still responsive: nothing about the refusal waited for the run.
        Assert.True(viewModel.IsBusy);

        reader.Release();
        await run;

        // Once the run has genuinely stopped, closing is allowed again.
        Assert.Equal(CheckState.Cancelled, viewModel.State);
        Assert.False(viewModel.TryBlockClose());
    }

    // ------------------------------------------------------------------ a source root that cannot be used

    [Fact]
    public async Task StartCheckAsync_WhenTheSourceRootDoesNotExist_ShowsASourceErrorInsteadOfAFailure()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var missingRoot = source.PathOf("does-not-exist");

        var viewModel = WorkflowInputs.ViewModelOver(WorkflowInputs.Over());
        viewModel.SourcePath = missingRoot;
        viewModel.TargetPath = target.Root;

        await viewModel.StartCheckAsync();

        // A folder that is not there is an expected outcome of the input: the screen reports a finished
        // assessment with a blocking finding rather than a check that broke.
        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.False(viewModel.HasFailure);
        Assert.Empty(viewModel.FailureText);
        Assert.DoesNotContain("Exception", viewModel.StateText);
        Assert.DoesNotContain("Exception", viewModel.SourceCoverageText);
        Assert.DoesNotContain("Exception", viewModel.ConclusionText);

        // The screen reports the blocking conclusion about the root, and beside it the coverage fact
        // that with no inventory there was nothing to check the destinations against. Neither is an
        // application failure, and neither carries the internal type that produced the refusal.
        Assert.Equal(
            ["SOURCE_ROOT_UNUSABLE", "TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED"],
            viewModel.Findings.Select(finding => finding.Code));

        var row = viewModel.Findings[0];
        Assert.Equal("SOURCE_ROOT_UNUSABLE", row.Code);
        Assert.Equal("Error", row.SeverityLabel);
        Assert.Equal("Source-wide (not tied to a single source entry)", row.SourcePathText);
        Assert.DoesNotContain("Exception", row.Explanation);

        var coverageGap = viewModel.Findings[1];
        Assert.Equal("Info", coverageGap.SeverityLabel);
        Assert.Contains("no source inventory", coverageGap.Explanation);
        Assert.DoesNotContain("Exception", coverageGap.Explanation);

        // A count that was never observed stays "not observed": a zero would state that the folder was
        // read and turned out to be empty, which is the opposite of what happened.
        Assert.Contains("not observed", viewModel.FilesText);
        Assert.DoesNotContain(": 0", viewModel.FilesText);
        Assert.Contains("source root could not be used", viewModel.CountsNote);

        // The coverage note says the observation was attempted and established nothing.
        Assert.Contains("incomplete", viewModel.SourceCoverageText);
        Assert.Contains("source root could not be used", viewModel.SourceCoverageText);

        // The strongest statement the product has is not available here: a blocking issue was found, so
        // the frozen sentence may not appear in any form.
        Assert.False(viewModel.HasBlockingStatement);
        Assert.Empty(viewModel.BlockingStatementText);
        Assert.DoesNotContain(PreflightText.NoKnownBlockingIssue, viewModel.ConclusionText);
        Assert.Contains("1 blocking issue", viewModel.ConclusionText);

        // Nothing was created to make the missing folder appear.
        Assert.False(Directory.Exists(missingRoot));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// A view model whose check completes over a scripted source listing and a scripted target seam.
    /// The source root is still a real directory, because the scanner checks that it is one.
    /// </summary>
    private static CheckViewModel CompletingViewModel(
        string sourceRoot,
        string targetRoot,
        ScriptedFileSystemApi? api = null,
        params ObservedDirectoryChild[] sourceChildren)
    {
        var reader = new ScriptedDirectoryReader().WithChildren(sourceRoot, sourceChildren);
        var workflow = WorkflowInputs.OverScriptedTarget(
            api ?? new ScriptedFileSystemApi().WithAttributes(FileAttributes.Directory),
            new SourceScanner(reader));

        var viewModel = WorkflowInputs.ViewModelOver(workflow);
        viewModel.SourcePath = sourceRoot;
        viewModel.TargetPath = targetRoot;

        return viewModel;
    }

    /// <summary>A view model that records every snapshot its run reports, and delivers them at once.</summary>
    private static CheckViewModel ViewModelRecordingProgress(
        List<PreflightRunProgress> observed,
        PreflightWorkflow? workflow = null)
        => new(
            workflow ?? new PreflightWorkflow(),
            handler => new SynchronousProgressSink<PreflightRunProgress>(snapshot =>
            {
                observed.Add(snapshot);
                handler(snapshot);
            }));
}

using System.IO;
using CopyPreflight.App;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The channel that carries progress snapshots to the interface.
/// </summary>
/// <remarks>
/// What is asserted here is the delivery, not the display: how many callbacks are waiting for the
/// interface thread, which snapshot those callbacks end up showing, and whether any snapshot is lost
/// on the way. The screen's own rules about which snapshot it accepts are asserted in
/// <see cref="CheckViewModelTests"/>.
/// </remarks>
public class CoalescingProgressTests
{
    [Fact]
    public void Report_WhileNothingHasBeenDeliveredYet_LeavesOneCallbackWaiting()
    {
        var context = new ManualSynchronizationContext();
        var delivered = new List<int>();
        var sink = new CoalescingProgress<int>(delivered.Add, context);

        for (var value = 1; value <= 1_000; value++)
        {
            sink.Report(value);
        }

        // A thousand snapshots have been published and the interface has not run once. What is waiting
        // for it is one callback, not a thousand: every snapshot after the first replaced the one
        // before it, because that is what "the newest state" means.
        Assert.Equal(1, context.PendingCount);
        Assert.Empty(delivered);
    }

    [Fact]
    public void DrainingTheQueue_ShowsTheNewestSnapshotAndNothingStaler()
    {
        var context = new ManualSynchronizationContext();
        var delivered = new List<int>();
        var sink = new CoalescingProgress<int>(delivered.Add, context);

        sink.Report(1);
        sink.Report(2);
        sink.Report(3);

        Assert.Equal(1, context.Drain());

        Assert.Equal(new[] { 3 }, delivered);
        Assert.Equal(0, context.PendingCount);
    }

    [Fact]
    public void Report_AfterTheQueueHasDrained_IsDeliveredAsWell()
    {
        var context = new ManualSynchronizationContext();
        var delivered = new List<int>();
        var sink = new CoalescingProgress<int>(delivered.Add, context);

        sink.Report(1);
        context.Drain();
        sink.Report(2);

        // An interface that keeps up loses nothing: a snapshot is only ever replaced when a newer one
        // was published before the older one got its turn.
        Assert.Equal(1, context.PendingCount);
        context.Drain();

        Assert.Equal(new[] { 1, 2 }, delivered);
    }

    [Fact]
    public void Report_FromInsideTheHandler_IsDeliveredRatherThanSwallowed()
    {
        var context = new ManualSynchronizationContext();
        var delivered = new List<int>();
        CoalescingProgress<int>? sink = null;

        // The handler publishing while it runs is the worst case for a channel that clears its "in
        // flight" flag at the wrong moment: the snapshot would be folded into the delivery that is
        // already handling an older one, and never shown.
        sink = new CoalescingProgress<int>(
            value =>
            {
                delivered.Add(value);

                if (value == 1)
                {
                    sink!.Report(2);
                }
            },
            context);

        sink.Report(1);
        context.Drain();

        Assert.Equal(new[] { 1, 2 }, delivered);
    }

    [Fact]
    public void WithoutASynchronizationContext_NothingIsMergedAway()
    {
        var delivered = new List<int>();
        var sink = new CoalescingProgress<int>(delivered.Add, context: null);

        sink.Report(1);
        sink.Report(2);
        sink.Report(3);

        // There is no queue and no second thread to hand anything to, so every snapshot is handled on
        // the thread that published it. Nothing can pile up, so nothing needs to be dropped.
        Assert.Equal(new[] { 1, 2, 3 }, delivered);
    }
}

/// <summary>
/// The queue in front of the interface, fed by a real scan.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests above state the channel's contract; these state what it does for the case that made
/// it necessary — a source tree that reports far more often than the interface can redraw, both as one
/// large directory and as many small ones. The scan, the target probes and the rule layer are the
/// production ones; only the directory listing is scripted, because a hundred thousand real files is
/// not something a test should have to create. The target is a real folder, and it is genuinely empty,
/// so every mapped destination is genuinely missing.
/// </para>
/// <para>
/// Nothing here sleeps or waits: the queue is read from inside the scan itself, on the thread the scan
/// runs on, which is the moment a backlog would be visible if there were one.
/// </para>
/// </remarks>
public class DenseProgressQueueTests
{
    private const int EntriesInOneDirectory = 20_000;

    private const int SmallDirectories = 250;

    [Fact]
    public void OneHugeDirectory_LeavesTheInterfaceQueueAtOneCallback()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var context = new ManualSynchronizationContext();
        var queueSizes = new List<int>();

        IEnumerable<ObservedDirectoryChild> Children()
        {
            for (var index = 0; index < EntriesInOneDirectory; index++)
            {
                yield return Observations.FileItem($"file{index:D5}.bin", 1L);

                // Runs when the scanner asks for the next entry, so it reads the queue after the report
                // that entry may have produced.
                queueSizes.Add(context.PendingCount);
            }
        }

        var reader = new ScriptedDirectoryReader().WithListing(source.Root, Children);
        var delivered = new List<PreflightRunProgress>();
        var sink = new CoalescingProgress<PreflightRunProgress>(delivered.Add, context);

        var result = WorkflowInputs
            .Over(new SourceScanner(reader))
            .Run(WorkflowInputs.Request(source.Root, target.Root), sink);

        Assert.Equal(PreflightRunStatus.Completed, result.Status);

        // The scan really did report repeatedly — this is a dense run, not one that happened to be quiet
        // enough for the question not to arise.
        Assert.Equal(EntriesInOneDirectory, queueSizes.Count);
        Assert.Contains(1, queueSizes);

        // And yet the interface never had more than one callback waiting for it.
        Assert.All(queueSizes, size => Assert.True(size <= 1, $"The interface queue held {size} callbacks."));
        Assert.Equal(1, context.PendingCount);
        Assert.Empty(delivered);
    }

    [Fact]
    public void ManySmallDirectories_LeaveTheInterfaceQueueAtOneCallback()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var context = new ManualSynchronizationContext();
        var queueSizes = new List<int>();

        var reader = new ScriptedDirectoryReader();
        var directories = new List<ObservedDirectoryChild>();

        for (var index = 0; index < SmallDirectories; index++)
        {
            var name = $"dir{index:D4}";
            directories.Add(Observations.DirectoryItem(name));

            // The listing is asked for once per directory, so it reads the queue once per directory —
            // after the report the previous directory produced.
            reader.WithListing(
                Path.Combine(source.Root, name),
                () =>
                {
                    queueSizes.Add(context.PendingCount);
                    return [Observations.FileItem("entry.bin", 1L)];
                });
        }

        reader.WithChildren(source.Root, [.. directories]);

        var delivered = new List<PreflightRunProgress>();
        var sink = new CoalescingProgress<PreflightRunProgress>(delivered.Add, context);

        var result = WorkflowInputs
            .Over(new SourceScanner(reader))
            .Run(WorkflowInputs.Request(source.Root, target.Root), sink);

        Assert.Equal(PreflightRunStatus.Completed, result.Status);

        // A report per directory is the ordinary case, and it is the one a tree of many small
        // directories turns into a flood.
        Assert.Equal(SmallDirectories, queueSizes.Count);
        Assert.Contains(1, queueSizes);
        Assert.All(queueSizes, size => Assert.True(size <= 1, $"The interface queue held {size} callbacks."));

        // The run reported on every stage and the interface still has a single callback to run — and the
        // snapshot it will show is the newest one, not the first of a backlog.
        Assert.Equal(1, context.Drain());
        Assert.Equal(PreflightRunPhase.EvaluatingRules, Assert.Single(delivered).Phase);
    }

    [Fact]
    public async Task TheViewModelsOwnProgressChannel_KeepsTheInterfaceQueueAtOneCallback()
    {
        // The two tests above build the channel directly. This one uses the channel the view model
        // builds for itself, which is the one production runs on: a run of the real view model over a
        // real scan, with the queue read from inside the scan. Without this, going back to a channel
        // that queues every snapshot would leave every test passing.
        const int EntryCount = 1_500;

        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var context = new ManualSynchronizationContext();
        var queueSizes = new List<int>();

        // Nothing is drained until the scan has said it is done, so the queue the scan reads is the queue
        // the scan itself produced. Draining while sampling would empty it from another thread and the
        // reading would be a race rather than a fact.
        using var scanFinished = new ManualResetEventSlim(false);

        IEnumerable<ObservedDirectoryChild> Children()
        {
            try
            {
                for (var index = 0; index < EntryCount; index++)
                {
                    yield return Observations.FileItem($"file{index:D5}.bin", 1L);
                    queueSizes.Add(context.PendingCount);
                }
            }
            finally
            {
                // Runs when the scanner is done with this listing, which is the end of the measured window.
                scanFinished.Set();
            }
        }

        var reader = new ScriptedDirectoryReader().WithListing(source.Root, Children);
        var viewModel = new CheckViewModel(WorkflowInputs.Over(new SourceScanner(reader)));
        viewModel.SourcePath = source.Root;
        viewModel.TargetPath = target.Root;

        var current = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var run = viewModel.StartCheckAsync();

            Assert.True(scanFinished.Wait(TimeSpan.FromMinutes(1)), "The scan should have finished.");

            // The run's continuation is queued on this same context, so the run can only finish once the
            // test drains it.
            while (!run.IsCompleted)
            {
                context.Drain();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(current);
        }

        Assert.Equal(EntryCount, queueSizes.Count);

        // Every entry after the first report found exactly one callback waiting — never none, never more
        // than one: the snapshots that arrived in between were folded into it rather than queued.
        var afterTheFirstReport = queueSizes.Skip(512).ToList();
        Assert.NotEmpty(afterTheFirstReport);
        Assert.All(afterTheFirstReport, size => Assert.Equal(1, size));

        // And the screen ends up showing the returned result, not a snapshot from the middle of the scan.
        Assert.Equal(CheckState.Completed, viewModel.State);
        Assert.Contains("Regular files: 1,500", viewModel.FilesText);
    }
}

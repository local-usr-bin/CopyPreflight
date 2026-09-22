using System.IO;
using CopyPreflight.App;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Records run progress synchronously, on whichever thread reported it.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> is deliberately not used here: it posts to a captured synchronization
/// context, so its callbacks can arrive after the run they describe has already returned. A test that
/// wants to say what a callback did has to own when it runs.
/// </remarks>
internal sealed class RecordingRunProgress : IProgress<PreflightRunProgress>
{
    private readonly Action<PreflightRunProgress>? _onReport;
    private readonly List<PreflightRunProgress> _snapshots = [];

    internal RecordingRunProgress(Action<PreflightRunProgress>? onReport = null) => _onReport = onReport;

    internal IReadOnlyList<PreflightRunProgress> Snapshots => _snapshots;

    public void Report(PreflightRunProgress value)
    {
        _snapshots.Add(value);
        _onReport?.Invoke(value);
    }
}

/// <summary>
/// A progress sink that runs the handler immediately, on the calling thread.
/// </summary>
/// <remarks>
/// The presentation layer's real sink posts to the synchronization context captured when a run starts,
/// which is right for a window and useless for a test: it makes the order in which snapshots are
/// applied depend on the thread pool. This sink keeps the same contract and delivers synchronously, so
/// a test can assert on the state machine without waiting for anything.
/// </remarks>
internal sealed class SynchronousProgressSink<T> : IProgress<T>
{
    private readonly Action<T> _onReport;

    internal SynchronousProgressSink(Action<T> onReport) => _onReport = onReport;

    public void Report(T value) => _onReport(value);
}

/// <summary>
/// A directory reader whose listing blocks until the test lets it go.
/// </summary>
/// <remarks>
/// Used to hold a check in a known state — running, and then waiting for a cancellation to be honoured
/// — without sleeping or guessing at timing. The block is released explicitly, so the test decides
/// when the work continues.
/// </remarks>
internal sealed class ControllableDirectoryReader : ISourceDirectoryReader
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(30);

    private readonly ManualResetEventSlim _entered = new(false);
    private readonly ManualResetEventSlim _released = new(false);
    private readonly List<string> _listedDirectories = [];

    /// <summary>Absolute paths the scanner asked to list, in the order it asked.</summary>
    internal IReadOnlyList<string> ListedDirectories
    {
        get
        {
            lock (_listedDirectories)
            {
                return [.. _listedDirectories];
            }
        }
    }

    /// <summary>Waits until a listing has begun.</summary>
    internal bool WaitUntilListingStarted() => _entered.Wait(ReleaseTimeout);

    /// <summary>Lets the listing return, so the run can continue.</summary>
    internal void Release() => _released.Set();

    public IEnumerable<ObservedDirectoryChild> ReadChildren(string absoluteDirectoryPath)
    {
        lock (_listedDirectories)
        {
            _listedDirectories.Add(absoluteDirectoryPath);
        }

        _entered.Set();
        _released.Wait(ReleaseTimeout);

        return [];
    }
}

/// <summary>
/// A directory reader whose first listing fails in a way the scanner does not absorb.
/// </summary>
/// <remarks>
/// The scanner only catches the file-system failures it is specified to recover from, so an ordinary
/// bug-type exception passes straight through it. That is what a run has to survive as a failed run
/// rather than as an empty result, and this reader is how the situation is produced without breaking
/// anything on disk.
/// </remarks>
internal sealed class FlakyDirectoryReader : ISourceDirectoryReader
{
    private readonly Exception _firstFailure;
    private bool _failed;

    internal FlakyDirectoryReader(Exception firstFailure) => _firstFailure = firstFailure;

    /// <summary>How many listings have been asked for.</summary>
    internal int ListingsRequested { get; private set; }

    public IEnumerable<ObservedDirectoryChild> ReadChildren(string absoluteDirectoryPath)
    {
        ListingsRequested++;

        if (!_failed)
        {
            _failed = true;
            throw _firstFailure;
        }

        return [];
    }
}

/// <summary>
/// Builds the requests, workflows and view models the workflow and presentation tests drive.
/// </summary>
internal static class WorkflowInputs
{
    /// <summary>A source root that is never touched on disk, for tests that script both sides.</summary>
    internal const string SourceRoot = @"C:\source";

    /// <summary>A target root that is never touched on disk, for tests that script the target side.</summary>
    internal const string TargetRoot = @"C:\target";

    /// <summary>Builds a request, failing the test when the paths are not acceptable inputs.</summary>
    internal static PreflightRequest Request(string sourcePath = SourceRoot, string targetPath = TargetRoot)
    {
        var created = PreflightRequest.TryCreate(sourcePath, targetPath, out var request, out _);

        Assert.True(created, $"'{sourcePath}' and '{targetPath}' should be acceptable inputs.");

        return request!;
    }

    /// <summary>A workflow over the real production components.</summary>
    internal static PreflightWorkflow Over(
        SourceScanner? scanner = null,
        WindowsTargetCapabilityProbe? capabilityProbe = null,
        WindowsTargetExistingObjectProbe? existingObjectProbe = null)
        => new(
            scanner ?? new SourceScanner(),
            capabilityProbe ?? new WindowsTargetCapabilityProbe(),
            existingObjectProbe ?? new WindowsTargetExistingObjectProbe(),
            new WindowsPreflightEvaluator());

    /// <summary>A workflow whose target side reads the scripted answers instead of the real file system.</summary>
    internal static PreflightWorkflow OverScriptedTarget(
        ScriptedFileSystemApi api,
        SourceScanner? scanner = null)
        => Over(
            scanner,
            capabilityProbe: new WindowsTargetCapabilityProbe(api),
            existingObjectProbe: new WindowsTargetExistingObjectProbe(api));

    /// <summary>
    /// A view model over the given workflow, with progress delivered synchronously so that its state
    /// machine can be asserted without waiting for a posted callback.
    /// </summary>
    internal static CheckViewModel ViewModelOver(PreflightWorkflow workflow)
        => new(workflow, handler => new SynchronousProgressSink<PreflightRunProgress>(handler));

    /// <summary>The mapped destination location a source relative path composes to under the test target.</summary>
    internal static string Mapped(string relativePath, string targetRoot = TargetRoot)
        => Path.Join(targetRoot, relativePath);
}

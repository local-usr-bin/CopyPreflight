using System.ComponentModel;
using System.Runtime.CompilerServices;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.App;

/// <summary>
/// Drives one check screen: it takes the two entered paths, runs the workflow, and turns what comes
/// back into text and rows a window can bind to.
/// </summary>
/// <remarks>
/// <para>
/// The view model contains no rule, no path probing and no file-system call of its own. It fixes the
/// inputs into a request, hands that request to the workflow, and reports what the workflow returned —
/// including the parts of it that are gaps rather than findings.
/// </para>
/// <para>
/// Three things are kept apart here exactly as they are in the workflow, because the screen must not
/// blur them either: the state of the screen (<see cref="CheckState"/>), what the check actually
/// covered, and the findings themselves. Nothing on this type derives a coverage statement from a
/// finding count, and nothing derives a finding from a state.
/// </para>
/// <para>
/// <b>One run at a time.</b> The screen becomes busy before the first suspension point, so a second
/// start cannot slip in behind the first, and while a run is active the inputs stay locked because a
/// result belongs to the request that produced it.
/// </para>
/// <para>
/// <b>A result belongs to its request.</b> Snapshots carry the identity of the request they describe;
/// a snapshot from an earlier run, or one that arrives after this run has already ended, is ignored
/// rather than applied to the wrong run or to a run that is over. The counts and findings finally
/// displayed come from the returned result, never from whichever snapshot happened to arrive last.
/// </para>
/// <para>
/// <b>Changing the input after a check discards its result.</b> Leaving a finished assessment on
/// screen under a path it was not made for is the one mistake this screen must never make, so editing
/// either path after a run returns the screen to its unchecked state.
/// </para>
/// <para>
/// The type is free of WPF types on purpose: its state machine is exercised without a window, and the
/// window only binds to it and forwards events. Whether a snapshot reaches it on the interface thread
/// is decided by the progress sink it is constructed with — the real one posts to the synchronization
/// context captured when the run starts.
/// </para>
/// </remarks>
public sealed class CheckViewModel : INotifyPropertyChanged
{
    private const string TargetNotCheckedReason =
        "The target was never probed, so nothing about it was established.";

    private readonly PreflightWorkflow _workflow;
    private readonly Func<Action<PreflightRunProgress>, IProgress<PreflightRunProgress>> _progressSinkFactory;

    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;
    private string _inputMessage = string.Empty;

    private CheckState _state = CheckState.Idle;
    private PreflightRunPhase _phase = PreflightRunPhase.ScanningSource;
    private Guid? _activeRequestId;
    private CancellationTokenSource? _cancellation;

    private string _stateText = PreflightText.DescribeState(CheckState.Idle);
    private string _phaseText = string.Empty;
    private string _sourcePathText = string.Empty;
    private string _targetPathText = string.Empty;
    private string _filesText = string.Empty;
    private string _directoriesText = string.Empty;
    private string _reparsePointsText = string.Empty;
    private string _logicalBytesText = string.Empty;
    private string _scanIssuesText = string.Empty;
    private string _countsNote = string.Empty;
    private string _fileSystemText = string.Empty;
    private string _freeSpaceText = string.Empty;
    private string _componentLimitText = string.Empty;
    private string _sourceCoverageText = string.Empty;
    private string _targetCoverageText = string.Empty;
    private string _existingObjectCoverageText = string.Empty;
    private string _rulesCoverageText = string.Empty;
    private string _conclusionText = string.Empty;
    private string _blockingStatementText = string.Empty;
    private bool _hasBlockingStatement;
    private string _failureText = string.Empty;
    private bool _hasFailure;
    private IReadOnlyList<FindingRow> _findings = [];
    private FindingRow? _selectedFinding;
    private string _findingsEmptyText = string.Empty;

    /// <summary>Creates a view model over the real workflow, reporting progress to the interface thread.</summary>
    public CheckViewModel()
        : this(new PreflightWorkflow())
    {
    }

    /// <summary>Creates a view model over a supplied workflow, reporting progress to the interface thread.</summary>
    /// <param name="workflow">The workflow that carries out the check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="workflow"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The progress sink is built when a run starts, which is on the interface thread, so
    /// <see cref="Progress{T}"/> captures the interface's synchronization context and its callbacks
    /// are already on the right thread. Nothing here reads or posts to a dispatcher itself.
    /// </remarks>
    public CheckViewModel(PreflightWorkflow workflow)
        : this(workflow, handler => new Progress<PreflightRunProgress>(handler))
    {
    }

    /// <summary>
    /// Creates a view model over a supplied workflow and progress sink. For tests only.
    /// </summary>
    /// <param name="workflow">The workflow that carries out the check.</param>
    /// <param name="progressSinkFactory">
    /// How a snapshot reaches this view model. The default posts it to the synchronization context
    /// captured when the run starts; a test supplies a sink that delivers it on the calling thread, so
    /// the state machine can be exercised without a window and without depending on when a posted
    /// callback happens to run.
    /// </param>
    internal CheckViewModel(
        PreflightWorkflow workflow,
        Func<Action<PreflightRunProgress>, IProgress<PreflightRunProgress>> progressSinkFactory)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(progressSinkFactory);

        _workflow = workflow;
        _progressSinkFactory = progressSinkFactory;
        ResetSummary();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The source folder as entered. Editing it discards a previous result.</summary>
    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                OnInputChanged();
            }
        }
    }

    /// <summary>The target folder as entered. Editing it discards a previous result.</summary>
    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetProperty(ref _targetPath, value))
            {
                OnInputChanged();
            }
        }
    }

    /// <summary>
    /// Why the entered paths were not accepted, or empty when nothing is wrong with them.
    /// </summary>
    public string InputMessage
    {
        get => _inputMessage;
        private set
        {
            if (SetProperty(ref _inputMessage, value))
            {
                OnPropertyChanged(nameof(HasInputMessage));
            }
        }
    }

    /// <summary>Whether there is something to say about the entered paths.</summary>
    public bool HasInputMessage => _inputMessage.Length > 0;

    /// <summary>What the screen is doing.</summary>
    public CheckState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                StateText = PreflightText.DescribeState(value);
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanStartCheck));
                OnPropertyChanged(nameof(CanCancelCheck));
                OnPropertyChanged(nameof(IsInputEnabled));
            }
        }
    }

    /// <summary>What the screen is doing, as it is displayed.</summary>
    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    /// <summary>The stage the check is in, or the stage it stopped in.</summary>
    public string PhaseText
    {
        get => _phaseText;
        private set => SetProperty(ref _phaseText, value);
    }

    /// <summary>Whether a check is running or waiting for a cancellation to take effect.</summary>
    public bool IsBusy => State is CheckState.Running or CheckState.Cancelling;

    /// <summary>Whether a new check may be started.</summary>
    public bool CanStartCheck => !IsBusy;

    /// <summary>Whether there is a running check to cancel.</summary>
    public bool CanCancelCheck => State == CheckState.Running;

    /// <summary>Whether the entered paths and the browse buttons may be edited.</summary>
    public bool IsInputEnabled => !IsBusy;

    /// <summary>The source folder the summary belongs to, with the path that was actually used.</summary>
    public string SourcePathText
    {
        get => _sourcePathText;
        private set => SetProperty(ref _sourcePathText, value);
    }

    /// <summary>The target folder the summary belongs to, with the path that was actually used.</summary>
    public string TargetPathText
    {
        get => _targetPathText;
        private set => SetProperty(ref _targetPathText, value);
    }

    /// <summary>How many regular files were observed. Reparse points are counted separately.</summary>
    public string FilesText
    {
        get => _filesText;
        private set => SetProperty(ref _filesText, value);
    }

    /// <summary>How many directories were observed, excluding the source root.</summary>
    public string DirectoriesText
    {
        get => _directoriesText;
        private set => SetProperty(ref _directoriesText, value);
    }

    /// <summary>How many reparse points were observed, of either shape.</summary>
    public string ReparsePointsText
    {
        get => _reparsePointsText;
        private set => SetProperty(ref _reparsePointsText, value);
    }

    /// <summary>The sum of the observed file lengths. It is not the space a copy would need.</summary>
    public string LogicalBytesText
    {
        get => _logicalBytesText;
        private set => SetProperty(ref _logicalBytesText, value);
    }

    /// <summary>How many observation failures the source scan recorded.</summary>
    public string ScanIssuesText
    {
        get => _scanIssuesText;
        private set => SetProperty(ref _scanIssuesText, value);
    }

    /// <summary>What the counts above do and do not cover, or empty when they are the whole picture.</summary>
    public string CountsNote
    {
        get => _countsNote;
        private set => SetProperty(ref _countsNote, value);
    }

    /// <summary>What file system Windows reported for the target, or that it could not be established.</summary>
    public string FileSystemText
    {
        get => _fileSystemText;
        private set => SetProperty(ref _fileSystemText, value);
    }

    /// <summary>How much space Windows reported as available to the current user on the target.</summary>
    public string FreeSpaceText
    {
        get => _freeSpaceText;
        private set => SetProperty(ref _freeSpaceText, value);
    }

    /// <summary>The longest single name the target accepts, or that it could not be established.</summary>
    public string ComponentLimitText
    {
        get => _componentLimitText;
        private set => SetProperty(ref _componentLimitText, value);
    }

    /// <summary>How much of the source tree was actually observed.</summary>
    public string SourceCoverageText
    {
        get => _sourceCoverageText;
        private set => SetProperty(ref _sourceCoverageText, value);
    }

    /// <summary>How many of the target's capabilities were actually established.</summary>
    public string TargetCoverageText
    {
        get => _targetCoverageText;
        private set => SetProperty(ref _targetCoverageText, value);
    }

    /// <summary>How much of the mapped destinations was actually examined.</summary>
    public string ExistingObjectCoverageText
    {
        get => _existingObjectCoverageText;
        private set => SetProperty(ref _existingObjectCoverageText, value);
    }

    /// <summary>Whether the findings were derived at all.</summary>
    public string RulesCoverageText
    {
        get => _rulesCoverageText;
        private set => SetProperty(ref _rulesCoverageText, value);
    }

    /// <summary>
    /// The conclusion the screen is allowed to draw, which depends on the run's state, on whether a
    /// blocking finding was produced and on whether the check left gaps.
    /// </summary>
    public string ConclusionText
    {
        get => _conclusionText;
        private set => SetProperty(ref _conclusionText, value);
    }

    /// <summary>
    /// The frozen "no known blocking issue" statement, or empty when it may not be shown. It is only
    /// ever shown for a check that finished without a blocking finding — never for a check that was
    /// cancelled, failed or never ran, whatever its finding list happens to contain.
    /// </summary>
    public string BlockingStatementText
    {
        get => _blockingStatementText;
        private set => SetProperty(ref _blockingStatementText, value);
    }

    /// <summary>Whether <see cref="BlockingStatementText"/> applies to what is on screen.</summary>
    public bool HasBlockingStatement
    {
        get => _hasBlockingStatement;
        private set => SetProperty(ref _hasBlockingStatement, value);
    }

    /// <summary>Why the check failed, or empty.</summary>
    public string FailureText
    {
        get => _failureText;
        private set => SetProperty(ref _failureText, value);
    }

    /// <summary>Whether the check failed.</summary>
    public bool HasFailure
    {
        get => _hasFailure;
        private set => SetProperty(ref _hasFailure, value);
    }

    /// <summary>Every finding the check produced, blocking first. Empty while no check has finished.</summary>
    public IReadOnlyList<FindingRow> Findings
    {
        get => _findings;
        private set
        {
            if (SetProperty(ref _findings, value))
            {
                OnPropertyChanged(nameof(HasFindings));
            }
        }
    }

    /// <summary>Whether there is anything in the findings list.</summary>
    public bool HasFindings => Findings.Count > 0;

    /// <summary>Whether the findings list is empty and the explanation of that emptiness applies.</summary>
    public bool IsFindingsEmpty => !HasFindings;

    /// <summary>The finding whose details are shown, or <see langword="null"/>.</summary>
    public FindingRow? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (SetProperty(ref _selectedFinding, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>Whether a finding is selected and its details are therefore shown.</summary>
    public bool HasSelection => SelectedFinding is not null;

    /// <summary>
    /// What an empty findings list means right now, which is different for a check that finished and
    /// found nothing and for one that never finished.
    /// </summary>
    public string FindingsEmptyText
    {
        get => _findingsEmptyText;
        private set => SetProperty(ref _findingsEmptyText, value);
    }

    /// <summary>
    /// Fixes the entered paths into a request and runs the check for it.
    /// </summary>
    /// <returns>A task that completes when the run has ended and its result is on screen.</returns>
    /// <remarks>
    /// <para>
    /// The screen becomes busy before the first suspension point, so a double click cannot start two
    /// checks: the second call sees a busy screen and returns.
    /// </para>
    /// <para>
    /// The entered paths are read once, here, and everything afterwards belongs to that snapshot. The
    /// previous result, its detail selection and its progress are cleared before the run starts, so
    /// nothing from an earlier request can survive next to the new one.
    /// </para>
    /// </remarks>
    public async Task StartCheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (!PreflightRequest.TryCreate(SourcePath, TargetPath, out var request, out var error))
        {
            InputMessage = PreflightText.DescribeRequestError(error);
            return;
        }

        InputMessage = string.Empty;
        _activeRequestId = request.RequestId;
        _phase = PreflightRunPhase.ScanningSource;
        ResetSummary();
        SourcePathText = PreflightText.DescribePathAgreement("Source folder", request.SourcePath, resolved: null);
        TargetPathText = PreflightText.DescribePathAgreement("Target folder", request.TargetPath, resolved: null);
        PhaseText = PreflightText.DescribePhase(_phase);

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        State = CheckState.Running;

        PreflightRunResult result;

        try
        {
            // The continuation is deliberately left on the captured context: the window it came from
            // is the only thing that may be updated once the run is over.
            result = await _workflow.RunAsync(request, _progressSinkFactory(ApplyProgress), cancellation.Token);
        }
        finally
        {
            // The token source belongs to this run alone and is released as soon as the run is over.
            // It is not disposed any earlier, because the stages still hold its token while they run.
            _cancellation = null;
            cancellation.Dispose();
        }

        ApplyResult(result);
    }

    /// <summary>
    /// Asks the running check to stop.
    /// </summary>
    /// <remarks>
    /// The screen changes to cancelling immediately, and stays busy until the run genuinely returns:
    /// the work does not stop the instant the button is pressed, and a second check must not be
    /// started on top of work that is still going on. A cancellation request that arrives when nothing
    /// is running does nothing at all, so a run that has already ended is never rewritten.
    /// </remarks>
    public void CancelCheck()
    {
        if (State != CheckState.Running)
        {
            return;
        }

        State = CheckState.Cancelling;
        _cancellation?.Cancel();
    }

    /// <summary>
    /// Whether an attempt to close the window must be blocked, and asks the running check to stop if so.
    /// </summary>
    /// <returns><see langword="true"/> when the close must be cancelled.</returns>
    /// <remarks>
    /// A check is never abandoned by hiding the window or by waiting for it on the interface thread:
    /// the close is refused, cancellation is requested, and the window stays responsive until the run
    /// has actually stopped, at which point closing it again succeeds. This covers ordinary window
    /// closing only — a forced process termination, a sign-out or a shutdown is not intercepted.
    /// </remarks>
    internal bool TryBlockClose()
    {
        if (!IsBusy)
        {
            return false;
        }

        CancelCheck();
        return true;
    }

    /// <summary>
    /// Applies one snapshot of a running check.
    /// </summary>
    /// <param name="progress">The snapshot, which carries the request it belongs to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A snapshot is accepted only if it belongs to the request currently on screen and only while that
    /// request is still running. That is what stops a callback from an earlier run from overwriting a
    /// later one, and what stops a snapshot that arrives after the run has ended from putting the
    /// screen back into a running state.
    /// </para>
    /// <para>
    /// Stages only move forwards, so a snapshot that arrives out of order cannot walk the displayed
    /// stage backwards. The counts shown here are what has been observed so far; the counts finally
    /// shown come from the returned result, not from the last snapshot to arrive.
    /// </para>
    /// </remarks>
    internal void ApplyProgress(PreflightRunProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (progress.RequestId != _activeRequestId || !IsBusy)
        {
            return;
        }

        if (progress.Phase < _phase)
        {
            return;
        }

        _phase = progress.Phase;
        PhaseText = PreflightText.DescribePhase(progress.Phase);

        if (progress.SourceScan is { } observed)
        {
            ShowObservedCounts(
                observed.FilesObserved,
                observed.DirectoriesObserved,
                observed.ReparsePointsObserved,
                observed.LogicalBytesObserved,
                observed.IssuesObserved,
                isPartial: true);
        }
    }

    /// <summary>Returns the screen to its unchecked state because the input no longer matches the result.</summary>
    private void OnInputChanged()
    {
        // While a check is running the inputs are disabled, so this cannot normally happen; if it
        // somehow does, the run in progress keeps its own fixed request and is left alone.
        if (IsBusy)
        {
            return;
        }

        InputMessage = string.Empty;
        _activeRequestId = null;
        ResetSummary();

        if (State != CheckState.Idle)
        {
            State = CheckState.Idle;
        }
    }

    /// <summary>Puts everything the summary and the findings list show back to "nothing checked yet".</summary>
    private void ResetSummary()
    {
        SourcePathText = PreflightText.DescribePathAgreement("Source folder", SourcePath, resolved: null);
        TargetPathText = PreflightText.DescribePathAgreement("Target folder", TargetPath, resolved: null);
        PhaseText = string.Empty;
        ShowCountsNotObserved();

        FileSystemText = PreflightText.DescribeFileSystemName(target: null);
        FreeSpaceText = PreflightText.DescribeFreeSpace(target: null);
        ComponentLimitText = PreflightText.DescribeComponentLimit(target: null);

        SourceCoverageText = PreflightText.DescribeCoverage("Source observation", PreflightPhaseCoverage.NotPerformed);
        TargetCoverageText = PreflightText.DescribeCoverage("Target capabilities", PreflightPhaseCoverage.NotPerformed);
        ExistingObjectCoverageText = PreflightText.DescribeCoverage(
            "Existing-object check",
            PreflightPhaseCoverage.NotPerformed);
        RulesCoverageText = PreflightText.DescribeCoverage("Findings", PreflightPhaseCoverage.NotPerformed);

        FailureText = string.Empty;
        HasFailure = false;
        Findings = [];
        SelectedFinding = null;
        FindingsEmptyText = DescribeEmptyFindings(CheckState.Idle, result: null);
        ShowConclusion(CheckState.Idle, result: null, errorCount: 0);
    }

    /// <summary>Shows the result of a finished run, replacing everything the summary was showing.</summary>
    private void ApplyResult(PreflightRunResult result)
    {
        // The result belongs to the request it was made for. If the entered paths no longer match that
        // request — which disabling the boxes while a check runs should make impossible, but which the
        // guarantee must not depend on — the result is not left standing under paths it was not made
        // for.
        if (!string.Equals(SourcePath, result.Request.SourcePath, StringComparison.Ordinal)
            || !string.Equals(TargetPath, result.Request.TargetPath, StringComparison.Ordinal))
        {
            _activeRequestId = null;
            ResetSummary();
            State = CheckState.Idle;

            return;
        }

        SourcePathText = PreflightText.DescribePathAgreement(
            "Source folder",
            result.Request.SourcePath,
            result.SourceScan?.SourceRoot);

        TargetPathText = PreflightText.DescribePathAgreement(
            "Target folder",
            result.Request.TargetPath,
            result.Target?.TargetDirectoryPath);

        ShowCounts(result);
        ShowTargetFacts(result.Target);
        ShowCoverage(result);

        var state = result.Status switch
        {
            PreflightRunStatus.Completed => CheckState.Completed,
            PreflightRunStatus.Cancelled => CheckState.Cancelled,
            _ => CheckState.Failed,
        };

        FailureText = result.Failure is null ? string.Empty : PreflightText.DescribeFailure(result.Failure);
        HasFailure = result.Failure is not null;
        PhaseText = result.StoppedInPhase is { } stopped
            ? $"Stopped during: {PreflightText.DescribePhase(stopped)}"
            : "All stages performed.";

        Findings = FindingRow.FromFindings(result.Findings);
        SelectedFinding = null;
        FindingsEmptyText = DescribeEmptyFindings(state, result);
        ShowConclusion(state, result, CountFindings(result.Findings, Severity.Error));

        // The screen's own state is set last, so that nothing the result carries is ever briefly shown
        // next to the previous run's state.
        State = state;
    }

    /// <summary>Shows the observed counts of a finished or interrupted run.</summary>
    private void ShowCounts(PreflightRunResult result)
    {
        if (result.SourceScan is { } scan)
        {
            ShowObservedCounts(
                scan.FileCount,
                scan.DirectoryCount,
                scan.ReparsePointCount,
                scan.LogicalFileBytes,
                scan.Issues.Count,
                isPartial: false);

            CountsNote = scan.IsComplete
                ? string.Empty
                : "The source was not observed in full, so these counts cover only the parts that could "
                    + "be read. The findings below state where the observation stopped.";

            return;
        }

        if (result.SourceObservedSoFar is { } observed)
        {
            ShowObservedCounts(
                observed.FilesObserved,
                observed.DirectoriesObserved,
                observed.ReparsePointsObserved,
                observed.LogicalBytesObserved,
                observed.IssuesObserved,
                isPartial: true);

            CountsNote = "The check stopped before the source observation finished, so these are counts "
                + "of what was seen, not an inventory of the source."
                + (result.SourceObservedSoFar.CurrentRelativePath is { } current
                    ? $" It stopped while observing '{current}'."
                    : string.Empty);

            return;
        }

        ShowCountsNotObserved();
        CountsNote = "The source was never observed, so nothing about its contents was established.";
    }

    /// <summary>Shows the target facts and what could not be established about the target.</summary>
    private void ShowTargetFacts(TargetProbeResult? target)
    {
        FileSystemText = PreflightText.DescribeFileSystemName(target);
        FreeSpaceText = PreflightText.DescribeFreeSpace(target);
        ComponentLimitText = PreflightText.DescribeComponentLimit(target);
    }

    /// <summary>Shows how much each stage of the run actually covered.</summary>
    private void ShowCoverage(PreflightRunResult result)
    {
        SourceCoverageText = PreflightText.DescribeCoverage(
            "Source observation",
            result.Coverage.SourceScan,
            DescribeSourceGap(result));

        TargetCoverageText = PreflightText.DescribeCoverage(
            "Target capabilities",
            result.Coverage.TargetCapabilities,
            DescribeTargetGap(result.Target));

        ExistingObjectCoverageText = PreflightText.DescribeCoverage(
            "Existing-object check",
            result.Coverage.ExistingObjects,
            DescribeExistingObjectGap(result.ExistingObjects));

        RulesCoverageText = PreflightText.DescribeCoverage("Findings", result.Coverage.Rules);
    }

    /// <summary>Why the source was not observed in full, from the issues the scan recorded.</summary>
    private static string? DescribeSourceGap(PreflightRunResult result)
    {
        if (result.SourceScan is { IsComplete: false } scan)
        {
            return $"{scan.Issues.Count} observation failure(s) were recorded";
        }

        // An observation that started and did not return is incomplete rather than never performed,
        // and says so here instead of leaving "incomplete" to be read as a vague warning.
        return result.SourceObservedSoFar is not null
            ? "the observation was stopped before it finished, so no inventory was produced"
            : null;
    }

    /// <summary>Which of the target's capabilities could not be established.</summary>
    private static string? DescribeTargetGap(TargetProbeResult? target)
    {
        if (target is null)
        {
            return null;
        }

        var capabilities = target.Capabilities;
        var unknown = 0;

        if (capabilities.FileSystemName.IsUnknown)
        {
            unknown++;
        }

        if (capabilities.AvailableFreeBytes.IsUnknown)
        {
            unknown++;
        }

        if (capabilities.MaxPathComponentLength.IsUnknown)
        {
            unknown++;
        }

        return unknown == 0 ? null : $"{unknown} of 3 capabilities could not be established";
    }

    /// <summary>How much of the mapped destinations the existing-object check left unexamined.</summary>
    private static string? DescribeExistingObjectGap(TargetExistingObjectProbeResult? existingObjects)
    {
        if (existingObjects is null)
        {
            return null;
        }

        if (existingObjects.Outcome == TargetExistingObjectProbeOutcome.Cancelled)
        {
            return "the check was cancelled before it finished";
        }

        if (existingObjects.Outcome != TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps)
        {
            return null;
        }

        var unexamined = existingObjects.Observations.Count(observation => observation.IsCoverageGap);

        return $"{unexamined} of {existingObjects.Observations.Count} mapped locations were left unexamined";
    }

    /// <summary>
    /// The one conclusion the screen may draw, and the only case in which the frozen statement applies.
    /// </summary>
    /// <remarks>
    /// The frozen statement is reachable only from a check that finished with no blocking finding. A
    /// cancelled run, a failed run, a run that has not been performed and a run still in progress all
    /// show what they are instead, however empty or non-empty their finding list happens to be.
    /// </remarks>
    private void ShowConclusion(CheckState state, PreflightRunResult? result, int errorCount)
    {
        if (state != CheckState.Completed || result is null)
        {
            BlockingStatementText = string.Empty;
            HasBlockingStatement = false;
            ConclusionText = state switch
            {
                CheckState.Running => "A check is running.",
                CheckState.Cancelling => "Cancellation requested. Nothing has been assessed yet.",
                CheckState.Cancelled =>
                    $"The check was cancelled, so {PreflightText.NotAnAssessment} The facts it did "
                    + "establish are shown above; they were not turned into findings.",
                CheckState.Failed =>
                    $"The check could not run to completion, so {PreflightText.NotAnAssessment} The facts "
                    + "it did establish are shown above; they were not turned into findings.",
                _ => "Nothing has been checked yet.",
            };

            return;
        }

        ConclusionText = errorCount > 0
            ? $"The assessment finished and found {errorCount} blocking issue(s)."
            : result.HasCoverageGaps
                ? PreflightText.CoverageGaps
                : "The assessment finished.";

        HasBlockingStatement = errorCount == 0;
        BlockingStatementText = HasBlockingStatement ? PreflightText.NoKnownBlockingIssue : string.Empty;
    }

    /// <summary>What an empty findings list means for the state the screen is in.</summary>
    private static string DescribeEmptyFindings(CheckState state, PreflightRunResult? result) => state switch
    {
        CheckState.Completed => result is { HasCoverageGaps: true }
            ? "No findings were produced, but the coverage notes above say which parts of the check were "
                + "not performed or not completed."
            : "No findings were produced. That means no known blocking issue was detected — it is not a "
                + "statement that a copy will succeed.",
        CheckState.Cancelled => "No findings are shown, because findings are derived only from a check "
            + "that finishes.",
        CheckState.Failed => "No findings are shown, because findings are derived only from a check that "
            + "finishes.",
        _ => "Nothing has been checked yet.",
    };

    /// <summary>Shows counts that were observed, marking them as partial when the observation was cut short.</summary>
    private void ShowObservedCounts(
        long files,
        long directories,
        long reparsePoints,
        long logicalBytes,
        long issues,
        bool isPartial)
    {
        FilesText = PreflightText.DescribeCount("Regular files", files, isPartial);
        DirectoriesText = PreflightText.DescribeCount(
            "Directories (excluding the source root)",
            directories,
            isPartial);
        ReparsePointsText = PreflightText.DescribeCount(
            "Reparse points (counted separately, not as files or directories)",
            reparsePoints,
            isPartial);
        LogicalBytesText = PreflightText.DescribeCount(
            "Logical file bytes (the total the files report, not the space a copy would need)",
            logicalBytes,
            isPartial);
        ScanIssuesText = PreflightText.DescribeCount("Source observation failures", issues, isPartial);
    }

    /// <summary>Shows that nothing was observed, rather than showing zeroes.</summary>
    private void ShowCountsNotObserved()
    {
        FilesText = PreflightText.DescribeNotObserved("Regular files");
        DirectoriesText = PreflightText.DescribeNotObserved(
            "Directories (excluding the source root)");
        ReparsePointsText = PreflightText.DescribeNotObserved(
            "Reparse points (counted separately, not as files or directories)");
        LogicalBytesText = PreflightText.DescribeNotObserved(
            "Logical file bytes (the total the files report, not the space a copy would need)");
        ScanIssuesText = PreflightText.DescribeNotObserved("Source observation failures");
        CountsNote = string.Empty;
    }

    /// <summary>Counts the findings of one severity.</summary>
    private static int CountFindings(IReadOnlyList<Finding> findings, Severity severity)
        => findings.Count(finding => finding.Severity == severity);

    /// <summary>Raises a property-changed notification.</summary>
    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets a field and raises a notification when the value actually changed.</summary>
    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }
}

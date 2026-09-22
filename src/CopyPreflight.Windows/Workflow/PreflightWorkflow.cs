using System.Runtime.Versioning;
using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Carries out one preflight check: it runs the observed facts of a source tree and a target
/// directory through the stages that turn them into findings, in the one order that works.
/// </summary>
/// <remarks>
/// <para>
/// The workflow is orchestration and nothing else. It owns no rule, observes nothing itself, and
/// decides nothing about the migration: it asks the scanner what is in the source, asks the target
/// probes what Windows can report about the destination, hands the facts to the rule layer, and
/// reports what came back. Every stage it calls is the real production implementation — there is no
/// substitute, no sample data and no shortcut in this path.
/// </para>
/// <para>
/// The sequence is fixed and each stage consumes what the previous one produced:
/// </para>
/// <list type="number">
/// <item><description>Observe the source tree.</description></item>
/// <item><description>Establish what Windows reports about the target directory itself.</description></item>
/// <item><description>Examine, read-only, what already occupies each mapped destination.</description></item>
/// <item><description>Derive the findings from all of the above.</description></item>
/// </list>
/// <para>
/// <b>A run that did not finish is not reported as an assessment.</b> Cancellation and unexpected
/// failure both stop the run and return a result that carries the facts already established, the
/// stages that were not reached, and no findings at all; the evaluation is only ever performed on a
/// run that got there. Nothing is re-observed to fill a gap, and no stage is re-run to look tidier.
/// </para>
/// <para>
/// <b>A stage that found nothing is not the same as a stage that never ran.</b> The result states
/// each stage's coverage independently, and a partial source scan or an unknown target capability is
/// carried through as a gap instead of being rounded off.
/// </para>
/// <para>
/// <b>Threading is decided here, once.</b> The scanner and both probes are synchronous, and the rule
/// layer is a pure computation, so <see cref="RunAsync"/> is the single place that moves the work off
/// the caller's thread. Nothing in this path touches a dispatcher, a window or a control, and the
/// progress channel is an <see cref="IProgress{T}"/> the caller provides.
/// </para>
/// <para>
/// The type holds no mutable state, so one instance can be reused for any number of runs.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PreflightWorkflow
{
    private readonly SourceScanner _scanner;
    private readonly WindowsTargetCapabilityProbe _capabilityProbe;
    private readonly WindowsTargetExistingObjectProbe _existingObjectProbe;
    private readonly WindowsPreflightEvaluator _evaluator;

    /// <summary>Creates a workflow over the real production components.</summary>
    public PreflightWorkflow()
        : this(
            new SourceScanner(),
            new WindowsTargetCapabilityProbe(),
            new WindowsTargetExistingObjectProbe(),
            new WindowsPreflightEvaluator())
    {
    }

    /// <summary>
    /// Creates a workflow over supplied components. For tests only.
    /// </summary>
    /// <remarks>
    /// The components are the production types, not abstractions of them: swapping an interface for
    /// each one would buy nothing, because a test that needs a controllable answer scripts the seam
    /// those types already expose internally. This constructor therefore exists so a test can build
    /// the same real workflow over a scripted file-system seam, not so the workflow can be replaced.
    /// </remarks>
    internal PreflightWorkflow(
        SourceScanner scanner,
        WindowsTargetCapabilityProbe capabilityProbe,
        WindowsTargetExistingObjectProbe existingObjectProbe,
        WindowsPreflightEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(capabilityProbe);
        ArgumentNullException.ThrowIfNull(existingObjectProbe);
        ArgumentNullException.ThrowIfNull(evaluator);

        _scanner = scanner;
        _capabilityProbe = capabilityProbe;
        _existingObjectProbe = existingObjectProbe;
        _evaluator = evaluator;
    }

    /// <summary>Runs one preflight check on a background thread.</summary>
    /// <param name="request">The fixed snapshot of what to examine.</param>
    /// <param name="progress">
    /// Optional receiver of stage and source-observation snapshots. It is called from the thread the
    /// check runs on, so an implementation that has to reach a user interface must arrange its own
    /// marshalling instead of assuming it is already there.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels this run. It is passed to every stage that supports cancellation, and checked between
    /// stages so that cancelling never starts a further stage.
    /// </param>
    /// <returns>
    /// The result of the run. It is never a faulted task: cancellation and unexpected failure are
    /// reported as results, so the caller has one shape of outcome to handle.
    /// </returns>
    /// <remarks>
    /// The token is deliberately not handed to the thread-pool hop. <see cref="Run"/> owns the token's
    /// whole policy, and a run whose token was already signalled must come back as a cancelled result
    /// rather than as a task that throws before it starts.
    /// </remarks>
    public Task<PreflightRunResult> RunAsync(
        PreflightRequest request,
        IProgress<PreflightRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() => Run(request, progress, cancellationToken), CancellationToken.None);
    }

    /// <summary>Runs one preflight check on the calling thread.</summary>
    /// <param name="request">The fixed snapshot of what to examine.</param>
    /// <param name="progress">Optional receiver of stage and source-observation snapshots.</param>
    /// <param name="cancellationToken">Cancels this run.</param>
    /// <returns>The result of the run; see <see cref="RunAsync"/> for its guarantees.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A source root that cannot be used at all — nothing is there, or it is not a directory — is a
    /// predictable outcome rather than a crash, and is reported as a failed run carrying the reason.
    /// It is deliberately <b>not</b> turned into a fabricated scan result: the scanner's contract keeps
    /// a precondition failure apart from a recoverable issue, and inventing an inventory the scanner
    /// never produced is exactly the kind of fact this tool must not manufacture.
    /// </para>
    /// <para>
    /// A cancellation reported against a different token is not this run's cancellation and is treated
    /// as an unexpected failure, so an unrelated cancellation cannot be presented as the user's. Nor is
    /// a cancellation carrying this run's own token when nothing has requested it: a run was cancelled
    /// by the user only if the token it was handed had actually been cancelled when the exception was
    /// raised. Both halves are required, because either one alone can be true for a stage that simply
    /// threw <see cref="OperationCanceledException"/> on its own.
    /// </para>
    /// </remarks>
    public PreflightRunResult Run(
        PreflightRequest request,
        IProgress<PreflightRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceObservation = new SourceObservationRelay(request, progress);

        SourceScanResult? sourceScan = null;
        TargetProbeResult? target = null;
        TargetExistingObjectProbeResult? existingObjects = null;
        var phase = PreflightRunPhase.ScanningSource;

        try
        {
            phase = PreflightRunPhase.ScanningSource;
            Report(progress, request, phase, sourceObservation.Latest);
            cancellationToken.ThrowIfCancellationRequested();
            sourceScan = _scanner.Scan(request.SourcePath, sourceObservation, cancellationToken);

            phase = PreflightRunPhase.ProbingTargetCapabilities;
            Report(progress, request, phase, sourceObservation.Latest);
            cancellationToken.ThrowIfCancellationRequested();
            target = _capabilityProbe.Probe(request.TargetPath);

            phase = PreflightRunPhase.ProbingExistingObjects;
            Report(progress, request, phase, sourceObservation.Latest);
            cancellationToken.ThrowIfCancellationRequested();

            // The existing-object facts are always obtained, and the evaluator is always given them.
            // A run that skipped this stage would report a coverage gap in place of a check, which is
            // not the same thing as a check that found nothing.
            existingObjects = _existingObjectProbe.Probe(sourceScan, target, cancellationToken);

            phase = PreflightRunPhase.EvaluatingRules;
            Report(progress, request, phase, sourceObservation.Latest);
            cancellationToken.ThrowIfCancellationRequested();
            var findings = _evaluator.Evaluate(sourceScan, target, existingObjects);

            return new PreflightRunResult(
                request,
                PreflightRunStatus.Completed,
                stoppedInPhase: null,
                sourceScan,
                sourceObservedSoFar: null,
                target,
                existingObjects,
                findings,
                failure: null);
        }
        catch (OperationCanceledException cancellation)
            when (cancellationToken.IsCancellationRequested
                && cancellation.CancellationToken == cancellationToken)
        {
            // The run was cancelled. What was established is kept; what was not returned stays
            // unestablished; and no finding is derived from the partial facts.
            return new PreflightRunResult(
                request,
                PreflightRunStatus.Cancelled,
                phase,
                sourceScan,
                sourceObservedSoFar: sourceScan is null ? sourceObservation.Latest : null,
                target,
                existingObjects,
                [],
                failure: null);
        }
        catch (Exception failure)
        {
            return new PreflightRunResult(
                request,
                PreflightRunStatus.Failed,
                phase,
                sourceScan,
                sourceObservedSoFar: sourceScan is null ? sourceObservation.Latest : null,
                target,
                existingObjects,
                [],
                new PreflightRunFailure(
                    phase,
                    failure.GetType().FullName ?? failure.GetType().Name,
                    failure.Message));
        }
    }

    /// <summary>Publishes the stage a run has reached, together with the latest source observation.</summary>
    private static void Report(
        IProgress<PreflightRunProgress>? progress,
        PreflightRequest request,
        PreflightRunPhase phase,
        SourceScanProgress? sourceObservation)
        => progress?.Report(new PreflightRunProgress(request.RequestId, phase, sourceObservation));
    /// <summary>
    /// Passes source-observation snapshots on to the caller's progress channel, remembering the latest
    /// one so the later stages can keep showing the source counters without re-deriving them.
    /// </summary>
    /// <remarks>
    /// It reports synchronously on whatever thread the scanner is running on. It never posts anywhere
    /// and never touches a dispatcher: making a snapshot reach a user interface is the caller's
    /// business, and a relay that guessed at a synchronization context would be doing that badly.
    /// </remarks>
    private sealed class SourceObservationRelay : IProgress<SourceScanProgress>
    {
        private readonly PreflightRequest _request;
        private readonly IProgress<PreflightRunProgress>? _progress;

        internal SourceObservationRelay(
            PreflightRequest request,
            IProgress<PreflightRunProgress>? progress)
        {
            _request = request;
            _progress = progress;
        }

        /// <summary>The most recent snapshot, or <see langword="null"/> when none has been reported.</summary>
        internal SourceScanProgress? Latest { get; private set; }

        public void Report(SourceScanProgress value)
        {
            Latest = value;

            _progress?.Report(new PreflightRunProgress(
                _request.RequestId,
                PreflightRunPhase.ScanningSource,
                value));
        }
    }
}

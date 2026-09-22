using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Everything one finished preflight check has to say.
/// </summary>
/// <remarks>
/// <para>
/// A result keeps three questions apart, because collapsing them into one word is how a report ends up
/// lying:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>How the run ended</b> — <see cref="Status"/>. A completed run, a cancelled one and one that hit
/// an unexpected failure are three different things.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>What was actually covered</b> — <see cref="Coverage"/>. A stage that was never performed and a
/// stage that ran and found nothing are different, and a source tree with an unreadable directory is
/// not the same as one that was fully observed.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>What risks were found</b> — <see cref="Findings"/>, each with its own severity.
/// </description>
/// </item>
/// </list>
/// <para>
/// There is no "success" flag, and no ordering of these three can be folded into one. In particular:
/// a completed run may carry Error findings (a destination that already holds an object of the other
/// kind is a real, decided conflict) while the application itself ran perfectly; and a completed run
/// with no findings at all may still have gaps behind it, so it supports no more than the statement
/// that no known blocking issue was detected.
/// </para>
/// <para>
/// <b>Findings are only derived from a run that finished.</b> A cancelled or failed run reports the
/// facts it managed to establish and the stages it never reached, but it produces no findings — a
/// partial evaluation presented next to a full one would be indistinguishable from it. The facts
/// themselves are kept: an interrupted source observation is carried as
/// <see cref="SourceObservedSoFar"/>, which is a count of what was seen, never an inventory.
/// </para>
/// <para>
/// The type is a plain, immutable value. It performs no I/O, holds no exception object, and carries no
/// UI type.
/// </para>
/// </remarks>
public sealed class PreflightRunResult
{
    /// <summary>Creates a run result and derives its coverage from the facts it is given.</summary>
    /// <param name="request">The request this run was carried out for.</param>
    /// <param name="status">How the run ended.</param>
    /// <param name="stoppedInPhase">
    /// The stage the run was in when it stopped, for a run that did not complete; <see langword="null"/>
    /// for a completed run.
    /// </param>
    /// <param name="sourceScan">
    /// What the source observation returned, or <see langword="null"/> when it did not return.
    /// </param>
    /// <param name="sourceObservedSoFar">
    /// The last source-observation snapshot, present only when that observation started but did not
    /// return. It is explicitly partial and is never an inventory.
    /// </param>
    /// <param name="target">What the target capability probe returned, or <see langword="null"/>.</param>
    /// <param name="existingObjects">
    /// What the existing-object check returned, or <see langword="null"/> when it did not return.
    /// </param>
    /// <param name="findings">
    /// The findings the rules produced. Empty for any run that did not complete.
    /// </param>
    /// <param name="failure">Why the run failed, for a failed run; otherwise <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="findings"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The facts contradict the status, the phase or each other — for example a completed run that did
    /// not obtain the source inventory, the target probe or the existing-object facts; a completed run
    /// carrying a failure; or an unfinished run claiming findings.
    /// </exception>
    /// <remarks>
    /// The contradictions are rejected here rather than discovered in a report, because every one of
    /// them would let a gap be presented as an assessment or an assessment as a gap.
    /// </remarks>
    public PreflightRunResult(
        PreflightRequest request,
        PreflightRunStatus status,
        PreflightRunPhase? stoppedInPhase,
        SourceScanResult? sourceScan,
        SourceScanProgress? sourceObservedSoFar,
        TargetProbeResult? target,
        TargetExistingObjectProbeResult? existingObjects,
        IReadOnlyList<Finding> findings,
        PreflightRunFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(findings);

        if (status == PreflightRunStatus.Completed)
        {
            if (sourceScan is null || target is null || existingObjects is null)
            {
                throw new ArgumentException(
                    "A completed run must have carried out every stage, so its source inventory, "
                    + "target capabilities and existing-object facts are all present.",
                    nameof(status));
            }

            if (failure is not null || stoppedInPhase is not null)
            {
                throw new ArgumentException(
                    "A completed run neither failed nor stopped in a stage.",
                    nameof(status));
            }
        }
        else
        {
            if (stoppedInPhase is null)
            {
                throw new ArgumentException(
                    "A run that did not complete must say which stage it stopped in.",
                    nameof(stoppedInPhase));
            }

            if (findings.Count > 0)
            {
                throw new ArgumentException(
                    "Findings are derived only from a run that finished; an unfinished run reports the "
                    + "facts and the gaps it has, not a partial verdict.",
                    nameof(findings));
            }

            if (sourceObservedSoFar is not null && sourceScan is not null)
            {
                throw new ArgumentException(
                    "A source observation that returned carries no 'observed so far' substitute for "
                    + "itself.",
                    nameof(sourceObservedSoFar));
            }
        }

        if (status == PreflightRunStatus.Failed && failure is null)
        {
            throw new ArgumentException("A failed run must say why it failed.", nameof(failure));
        }

        if (status != PreflightRunStatus.Failed && failure is not null)
        {
            throw new ArgumentException("Only a failed run carries a failure.", nameof(failure));
        }

        if (existingObjects is not null && target is null)
        {
            throw new ArgumentException(
                "The mapped destinations are derived from the target, so they cannot have been examined "
                + "without the target having been probed.",
                nameof(existingObjects));
        }

        Request = request;
        Status = status;
        StoppedInPhase = stoppedInPhase;
        SourceScan = sourceScan;
        SourceObservedSoFar = sourceObservedSoFar;
        Target = target;
        ExistingObjects = existingObjects;
        Findings = findings;
        Failure = failure;
        Coverage = DescribeCoverage(sourceScan, sourceObservedSoFar, target, existingObjects, status);
    }

    /// <summary>The request this run was carried out for, with its identity and both entered paths.</summary>
    public PreflightRequest Request { get; }

    /// <summary>How the run ended.</summary>
    public PreflightRunStatus Status { get; }

    /// <summary>
    /// The stage the run was in when it stopped; <see langword="null"/> when it ran to the end.
    /// </summary>
    public PreflightRunPhase? StoppedInPhase { get; }

    /// <summary>
    /// What the source observation returned, or <see langword="null"/> when it never returned. This is
    /// the only object that may be called an inventory; see <see cref="SourceObservedSoFar"/> for what
    /// remains when it is absent.
    /// </summary>
    public SourceScanResult? SourceScan { get; }

    /// <summary>
    /// The last source-observation snapshot, present only when that observation started but did not
    /// return.
    /// </summary>
    /// <remarks>
    /// These counters describe what was seen before the run stopped. They are not an inventory, they
    /// carry no entries and no issues, and nothing was evaluated from them.
    /// </remarks>
    public SourceScanProgress? SourceObservedSoFar { get; }

    /// <summary>What the target capability probe established, or <see langword="null"/>.</summary>
    public TargetProbeResult? Target { get; }

    /// <summary>What the existing-object check established, or <see langword="null"/>.</summary>
    public TargetExistingObjectProbeResult? ExistingObjects { get; }

    /// <summary>
    /// The findings the rules produced, in the order the rules ran. Empty when nothing was found and
    /// also when nothing was derived; <see cref="Coverage"/> and <see cref="Status"/> say which.
    /// </summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>Why the run failed, for a failed run; otherwise <see langword="null"/>.</summary>
    public PreflightRunFailure? Failure { get; }

    /// <summary>What each stage of this run actually covered.</summary>
    public PreflightCoverage Coverage { get; }

    /// <summary>Whether any stage left part of its scope unobserved, or was never performed at all.</summary>
    public bool HasCoverageGaps => Coverage.HasGaps;

    /// <summary>
    /// Describes what was covered, derived from the facts and outcomes themselves so that the
    /// description cannot disagree with them.
    /// </summary>
    /// <remarks>
    /// Nothing here reads the findings. An informational finding is not evidence of a gap, and the
    /// absence of an Error is not evidence that everything was observed.
    /// </remarks>
    private static PreflightCoverage DescribeCoverage(
        SourceScanResult? sourceScan,
        SourceScanProgress? sourceObservedSoFar,
        TargetProbeResult? target,
        TargetExistingObjectProbeResult? existingObjects,
        PreflightRunStatus status)
    {
        return new PreflightCoverage(
            // The source is fully observed when the scanner recorded no issue at all. That is the
            // scanner's own definition of completeness, reused rather than restated.
            //
            // An observation that started and did not return is deliberately not "not performed": it
            // went over part of the tree and was then stopped, and the counters it left behind are
            // facts about it. Reporting it as never performed would contradict the "observed so far"
            // counts shown beside it, and would understate how much of the source was reached.
            sourceScan switch
            {
                { IsComplete: true } => PreflightPhaseCoverage.Complete,
                { } => PreflightPhaseCoverage.Incomplete,
                _ => sourceObservedSoFar is null
                    ? PreflightPhaseCoverage.NotPerformed
                    : PreflightPhaseCoverage.Incomplete,
            },
            DescribeTargetCoverage(target),
            existingObjects switch
            {
                null or { Outcome: TargetExistingObjectProbeOutcome.NotPerformed }
                    => PreflightPhaseCoverage.NotPerformed,
                { Outcome: TargetExistingObjectProbeOutcome.Completed } => PreflightPhaseCoverage.Complete,
                _ => PreflightPhaseCoverage.Incomplete,
            },
            status == PreflightRunStatus.Completed
                ? PreflightPhaseCoverage.Complete
                : PreflightPhaseCoverage.NotPerformed);
    }

    /// <summary>
    /// Whether the target's capabilities were all established, only partly established, or never
    /// asked about.
    /// </summary>
    /// <remarks>
    /// A capability that stayed unknown is a gap in what is known about the target, not a failure of
    /// the check: the three capabilities are independent, and a target Windows will not describe in
    /// full is an ordinary outcome. Every capability the model has is required here, so adding one to
    /// <see cref="TargetCapabilities"/> cannot silently stop being covered.
    /// </remarks>
    private static PreflightPhaseCoverage DescribeTargetCoverage(TargetProbeResult? target)
    {
        if (target is null)
        {
            return PreflightPhaseCoverage.NotPerformed;
        }

        var capabilities = target.Capabilities;

        return capabilities.FileSystemName.IsKnown
            && capabilities.AvailableFreeBytes.IsKnown
            && capabilities.MaxPathComponentLength.IsKnown
                ? PreflightPhaseCoverage.Complete
                : PreflightPhaseCoverage.Incomplete;
    }
}

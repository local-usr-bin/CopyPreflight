namespace CopyPreflight.Windows;

/// <summary>
/// Everything one read-only check of the mapped destination locations established.
/// </summary>
/// <remarks>
/// <para>
/// The result is a plain, inspectable value: the outcome of the run, what was established about the
/// target root, and one observation per source object in the inventory it was given. It contains no
/// <see cref="Finding"/> and no severity — deciding what any of it means for a migration is the rule
/// layer's job.
/// </para>
/// <para>
/// What it says is deliberately narrow. It reports whether an object already occupies each mapped
/// location, and what kind of object that is. It does <b>not</b> compare any content, does not read
/// any file, does not decide whether an existing object should be overwritten or skipped, and does
/// not say that the target is writable: no probe file is created, no directory is made and no ACL is
/// changed, so no write capability was ever exercised.
/// </para>
/// <para>
/// Coverage is part of the result rather than an afterthought. An object that was never examined is
/// recorded as unexamined — never as absent — and the outcome distinguishes a run that covered the
/// inventory from one that did not, so a report can never present a gap as a clean finding of no
/// conflicts.
/// </para>
/// </remarks>
public sealed class TargetExistingObjectProbeResult
{
    /// <summary>Creates a result from the facts one run established.</summary>
    /// <param name="targetRoot">What the target root's own attribute query established.</param>
    /// <param name="observations">One observation per source object in the inventory, in any order.</param>
    /// <param name="outcome">How far the run got.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="targetRoot"/> or <paramref name="observations"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcome"/> contradicts the facts supplied: a run that was not performed cannot
    /// carry observations, a <see cref="TargetExistingObjectProbeOutcome.Completed"/> run cannot leave
    /// a location uncovered or the target root unusable, and a run reported as having coverage gaps
    /// must have one.
    /// </exception>
    /// <remarks>
    /// The outcome is not taken on trust. A result that claimed to be complete while carrying an
    /// unexamined location would misrepresent the very thing this model exists to keep honest, so the
    /// contradiction is rejected here rather than discovered later in a report.
    /// </remarks>
    public TargetExistingObjectProbeResult(
        TargetRootObservation targetRoot,
        IReadOnlyList<ExistingObjectObservation> observations,
        TargetExistingObjectProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(targetRoot);
        ArgumentNullException.ThrowIfNull(observations);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown outcome.");
        }

        if (outcome == TargetExistingObjectProbeOutcome.NotPerformed && observations.Count > 0)
        {
            throw new ArgumentException(
                "A check that was not performed cannot have observed anything.",
                nameof(observations));
        }

        var covered = HasFullCoverage(targetRoot, observations);

        if (outcome == TargetExistingObjectProbeOutcome.Completed && !covered)
        {
            throw new ArgumentException(
                "A completed check must cover every mapped location and must have been able to examine "
                + "the target root; record the gaps instead of reporting completion.",
                nameof(outcome));
        }

        if (outcome == TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps && covered)
        {
            throw new ArgumentException(
                "A check reported as having coverage gaps must actually leave something uncovered.",
                nameof(outcome));
        }

        TargetRoot = targetRoot;
        Observations = observations;
        Outcome = outcome;
    }

    /// <summary>What the target root's own attribute query established.</summary>
    public TargetRootObservation TargetRoot { get; }

    /// <summary>
    /// One observation per source object the check was given. The observation's
    /// <see cref="ExistingObjectObservation.SourceRelativePath"/> relates it to its source entry, so
    /// the order is not meaningful and a report must never depend on it.
    /// </summary>
    public IReadOnlyList<ExistingObjectObservation> Observations { get; }

    /// <summary>
    /// How far the run got. Only <see cref="TargetExistingObjectProbeOutcome.Completed"/> describes a
    /// run that covered every mapped location, and even that is not a claim that a copy will succeed.
    /// </summary>
    public TargetExistingObjectProbeOutcome Outcome { get; }

    /// <summary>Whether the check covered every mapped location and could examine the target root.</summary>
    public bool IsComplete => Outcome == TargetExistingObjectProbeOutcome.Completed;

    /// <summary>
    /// The result of a check that was never carried out.
    /// </summary>
    /// <returns>
    /// A result with no observations and
    /// <see cref="TargetExistingObjectProbeOutcome.NotPerformed"/>. It exists so that "no check was
    /// supplied" is an explicit fact the evaluator can report, rather than a silence that could be
    /// mistaken for a clean result.
    /// </returns>
    public static TargetExistingObjectProbeResult NotPerformed()
        => new(
            TargetRootObservation.NotAttempted(
                "No existing-object check was carried out, so no mapped location was examined."),
            [],
            TargetExistingObjectProbeOutcome.NotPerformed);

    /// <summary>
    /// Whether every mapped location is covered, which requires both that the target root could be
    /// examined and that no observation left its location unchecked.
    /// </summary>
    /// <remarks>
    /// A root whose attribute query did not answer still counts as examinable: the target's usability
    /// was settled from positive evidence before this check ran, and Microsoft documents that the root
    /// of a network share does not answer the attribute query. A root Windows positively reported as a
    /// file or as a reparse point is a different matter — nothing below it was examined at all.
    /// </remarks>
    internal static bool HasFullCoverage(
        TargetRootObservation targetRoot,
        IReadOnlyList<ExistingObjectObservation> observations)
    {
        if (targetRoot.Kind is not (TargetRootObservationKind.Directory
            or TargetRootObservationKind.NotAnswered))
        {
            return false;
        }

        foreach (var observation in observations)
        {
            if (observation.IsCoverageGap)
            {
                return false;
            }
        }

        return true;
    }
}

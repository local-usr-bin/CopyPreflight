namespace CopyPreflight.Windows;

/// <summary>
/// How far one run of the existing-object check got.
/// </summary>
/// <remarks>
/// <para>
/// The states exist because "the loop finished" is not the same claim as "every mapped location was
/// examined", and a report that confused the two would turn a gap into an assurance. In particular:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="NotPerformed"/> means no check was supplied at all. It must never be read as a clean
/// result, and the rule layer states the gap in the report so that it cannot be.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Completed"/> means every mapped location was covered — examined directly, or covered by
/// a sound inference from an absent ancestor, or stopped by an ancestor that is an ordinary file,
/// which is a confirmed structural conflict reported where it occurs. It is still <i>not</i> a claim
/// that the migration will succeed.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="CompletedWithCoverageGaps"/> means the check ran to the end but some locations were
/// left unexamined or could not be answered. The unchecked locations are facts, not absences.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Cancelled"/> means the run was interrupted before it finished. The locations that were
/// never reached are unknown, never absent, and the run must not be described as complete.
/// </description>
/// </item>
/// </list>
/// <para>
/// The check itself propagates <see cref="OperationCanceledException"/> when its cancellation token is
/// signalled, exactly as the rest of this codebase does, so a cancelled run never presents itself as a
/// finished one. <see cref="Cancelled"/> is the state a caller that caught that cancellation uses to
/// describe the partial run it did observe; the evaluator handles it and never treats it as
/// completion.
/// </para>
/// </remarks>
public enum TargetExistingObjectProbeOutcome
{
    /// <summary>No existing-object check was carried out.</summary>
    NotPerformed = 0,

    /// <summary>The check ran and covered every mapped location.</summary>
    Completed = 1,

    /// <summary>The check ran to the end, but part of the inventory is not covered.</summary>
    CompletedWithCoverageGaps = 2,

    /// <summary>The check was interrupted before it finished.</summary>
    Cancelled = 3,
}

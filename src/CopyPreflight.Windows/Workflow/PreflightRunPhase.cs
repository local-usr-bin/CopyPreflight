namespace CopyPreflight.Windows;

/// <summary>
/// The stages one preflight check performs, in the order it performs them.
/// </summary>
/// <remarks>
/// <para>
/// The order of the members is the order of execution, and it is the only order that makes sense:
/// the source inventory is what the mapped destinations are derived from, the target's own
/// capabilities decide whether those destinations can be reasoned about at all, and the rules consume
/// the facts the earlier stages produced. A stage that could not be reached is not silently skipped —
/// the result says which stages were not performed.
/// </para>
/// <para>
/// Nothing here is a severity, and a run reaching a later stage says nothing about whether the earlier
/// ones observed everything they set out to observe. Completion and coverage are separate questions
/// and are answered separately.
/// </para>
/// </remarks>
public enum PreflightRunPhase
{
    /// <summary>Observing what the source tree actually contains.</summary>
    ScanningSource = 0,

    /// <summary>Establishing what Windows can report about the target directory itself.</summary>
    ProbingTargetCapabilities = 1,

    /// <summary>Establishing, read-only, what already occupies each mapped destination.</summary>
    ProbingExistingObjects = 2,

    /// <summary>Deriving the findings from the facts the stages above established.</summary>
    EvaluatingRules = 3,
}

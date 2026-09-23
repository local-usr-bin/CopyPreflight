namespace CopyPreflight.Windows;

/// <summary>
/// Whether one stage of a check was performed, and whether it observed everything it set out to
/// observe.
/// </summary>
/// <remarks>
/// <para>
/// The distinction between <see cref="NotPerformed"/> and a stage that ran but produced nothing is the
/// whole point of this type. "Nothing was found" and "nothing was looked at" are different statements,
/// and a report that renders both as an empty list would be lying about one of them.
/// </para>
/// <para>
/// <see cref="Incomplete"/> is not a failure of the application. A source tree with an unreadable
/// directory, a target whose file system name Windows will not report, and a destination check that
/// stopped at a reparse point are all normal outcomes that leave a real gap in the assessment; they
/// are stated as gaps rather than being rounded up to "complete" or down to "not performed".
/// </para>
/// </remarks>
public enum PreflightPhaseCoverage
{
    /// <summary>The stage was never reached, so it established nothing at all.</summary>
    NotPerformed = 0,

    /// <summary>The stage ran and observed everything within its scope.</summary>
    Complete = 1,

    /// <summary>The stage ran but part of its scope was not observed. The gap is a fact of its own.</summary>
    Incomplete = 2,
}

/// <summary>
/// What each stage of one check actually covered.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of the three things a run reports, and it is deliberately separate from the
/// other two: <see cref="PreflightRunStatus"/> says how the run ended, and the findings say what risks
/// were found. A completed run with no findings and a completed run with no findings <i>and</i> three
/// coverage gaps are very different results, and only this type tells them apart.
/// </para>
/// <para>
/// Nothing here is derived from what the findings happen to contain. A gap is recorded from the facts
/// and the outcomes of the stages themselves — an issue the scanner recorded, a capability that stayed
/// unknown, an observation that left a location unexamined — never inferred from the absence or the
/// presence of an informational finding.
/// </para>
/// </remarks>
public sealed record PreflightCoverage
{
    /// <summary>Creates a coverage description from the four stages of a check.</summary>
    /// <param name="sourceScan">Whether the source was observed, and whether all of it was.</param>
    /// <param name="targetCapabilities">Whether the target's capabilities were established.</param>
    /// <param name="existingObjects">Whether the mapped destinations were examined.</param>
    /// <param name="rules">Whether the findings were derived from the facts.</param>
    public PreflightCoverage(
        PreflightPhaseCoverage sourceScan,
        PreflightPhaseCoverage targetCapabilities,
        PreflightPhaseCoverage existingObjects,
        PreflightPhaseCoverage rules)
    {
        SourceScan = sourceScan;
        TargetCapabilities = targetCapabilities;
        ExistingObjects = existingObjects;
        Rules = rules;
    }

    /// <summary>Whether the source was observed completely, partially, or not at all.</summary>
    public PreflightPhaseCoverage SourceScan { get; }

    /// <summary>
    /// Whether the target's capabilities were all established, only partly established, or not
    /// established at all.
    /// </summary>
    public PreflightPhaseCoverage TargetCapabilities { get; }

    /// <summary>Whether the mapped destinations were examined completely, partially, or not at all.</summary>
    public PreflightPhaseCoverage ExistingObjects { get; }

    /// <summary>
    /// Whether the findings were derived. This stage either ran or did not; how complete the facts it
    /// consumed were is stated by the three stages above it.
    /// </summary>
    public PreflightPhaseCoverage Rules { get; }

    /// <summary>
    /// Whether any stage of this check left part of its scope unobserved, or was never performed.
    /// </summary>
    public bool HasGaps => SourceScan != PreflightPhaseCoverage.Complete
        || TargetCapabilities != PreflightPhaseCoverage.Complete
        || ExistingObjects != PreflightPhaseCoverage.Complete
        || Rules != PreflightPhaseCoverage.Complete;

    /// <summary>
    /// Whether any stage of this check was never carried out at all.
    /// </summary>
    /// <remarks>
    /// A stage can be left undone in a run that finished — most visibly when an unusable source root
    /// leaves nothing for the existing-object check to examine — so a finished run is not by itself
    /// evidence that every stage happened. This is deliberately narrower than <see cref="HasGaps"/>:
    /// a stage that ran and could not observe everything was still performed, and saying otherwise
    /// would understate what the check did reach.
    /// </remarks>
    public bool HasStageNotPerformed => SourceScan == PreflightPhaseCoverage.NotPerformed
        || TargetCapabilities == PreflightPhaseCoverage.NotPerformed
        || ExistingObjects == PreflightPhaseCoverage.NotPerformed
        || Rules == PreflightPhaseCoverage.NotPerformed;
}

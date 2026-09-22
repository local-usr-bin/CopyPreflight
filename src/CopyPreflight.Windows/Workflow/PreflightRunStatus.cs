namespace CopyPreflight.Windows;

/// <summary>
/// How far one preflight check got.
/// </summary>
/// <remarks>
/// <para>
/// This is a fact about the <b>run</b>, not about the migration. <see cref="Completed"/> means every
/// stage was performed and the findings were derived from the facts those stages established; it does
/// <b>not</b> mean the findings are reassuring, and it does not mean the check observed everything it
/// wanted to — that is stated separately by <see cref="PreflightCoverage"/>.
/// </para>
/// <para>
/// The intermediate states a user sees while a check is running — waiting, running, cancellation
/// requested — belong to whatever is driving the run and are not part of this enum: by the time a
/// result exists, the run has stopped, and it stopped in exactly one of these three ways.
/// </para>
/// </remarks>
public enum PreflightRunStatus
{
    /// <summary>Every stage ran and the findings were derived from what they established.</summary>
    Completed = 0,

    /// <summary>
    /// The run was cancelled before it finished. Nothing was derived from the partial facts, and the
    /// stages that did not run remain unperformed.
    /// </summary>
    Cancelled = 1,

    /// <summary>
    /// The run stopped on an unexpected failure. Whatever facts were already established are kept;
    /// nothing was derived from them.
    /// </summary>
    Failed = 2,
}

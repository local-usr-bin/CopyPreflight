namespace CopyPreflight.Windows;

/// <summary>
/// How the state of one mapped destination location came to be known — or why it is still not known.
/// </summary>
/// <remarks>
/// <para>
/// These kinds exist because several outcomes that look alike in a summary are genuinely different
/// facts, and conflating them would turn a gap into a conclusion:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>A location that was queried and answered</b> is an observation. Whether an object is there, and
/// what kind it is, was established directly.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A location that was queried and answered "nothing is there"</b> is also an observation, but it
/// is kept apart from the one above because it is evidence of absence rather than of presence, and
/// because the evidence behind it is a specific answered error rather than an attribute result.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A location whose ancestor was established as absent</b> is <i>not</i> the same thing as one
/// that was queried: nothing was asked about this path. It is a conclusion derived from a fact about
/// a different path, and it is labelled as such so that no reader mistakes it for a per-location
/// query result.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A location whose query did not answer</b> leaves both existence and kind unknown. It is never
/// downgraded to "absent", because a check that failed is not evidence about what is there.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A location that was not queried at all</b> is a coverage gap with a reason. No question was
/// asked, so no answer is implied.
/// </description>
/// </item>
/// </list>
/// <para>
/// This is <b>not</b> a severity scale. Deciding what any of these facts means for a migration
/// belongs to the rule layer, and no kind carries a <see cref="CopyPreflight.Core.Severity"/>.
/// </para>
/// </remarks>
public enum ExistingObjectObservationKind
{
    /// <summary>
    /// The mapped location was queried and answered: an object is there, and its kind is recorded in
    /// <see cref="ExistingObjectObservation.Presence"/>.
    /// </summary>
    Observed = 0,

    /// <summary>
    /// The mapped location was queried, and the answer was positive evidence that no object is there.
    /// It says nothing about whether the location could be created or written to.
    /// </summary>
    ObservedMissing = 1,

    /// <summary>
    /// No query was made about this location because an ancestor was established as absent: nothing
    /// can exist below a path component that is not there. This is a derived conclusion, not a
    /// per-location query, and it is deliberately not reported as one.
    /// </summary>
    InferredMissing = 2,

    /// <summary>
    /// The query about this location did not answer, so both existence and kind remain unknown. The
    /// reported native error code is the evidence; the absence of an answer is not evidence of
    /// absence.
    /// </summary>
    CheckFailed = 3,

    /// <summary>
    /// No query was made about this location. <see cref="ExistingObjectObservation.GapReason"/>
    /// states why, which is what makes this different from <see cref="ObservedMissing"/>.
    /// </summary>
    NotQueried = 4,
}

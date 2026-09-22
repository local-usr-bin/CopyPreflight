namespace CopyPreflight.Windows;

/// <summary>
/// Why a mapped destination location is not covered by a direct observation.
/// </summary>
/// <remarks>
/// <para>
/// Every reason names a specific cause so that a coverage gap can be explained without being
/// re-stated per affected object, and so that a gap caused by this milestone's own scope is never
/// confused with one caused by the file system.
/// </para>
/// <para>
/// The reasons are not interchangeable, and in particular none of them means "the location is
/// empty". A gap is the absence of a fact, not a fact about absence.
/// </para>
/// </remarks>
public enum ExistingObjectGapReason
{
    /// <summary>
    /// An ancestor was established as absent, so nothing can currently exist at this location. Used
    /// only by <see cref="ExistingObjectObservationKind.InferredMissing"/>, and deliberately distinct
    /// from a per-location query that answered "nothing is there".
    /// </summary>
    MissingAncestor = 0,

    /// <summary>
    /// An ancestor already holds a regular file. A file cannot contain anything, so no location below
    /// it can hold an object. The branch is not queried further and the structural conflict is
    /// reported once, at the ancestor, instead of once per descendant.
    /// </summary>
    BlockedByExistingObject = 1,

    /// <summary>
    /// An ancestor is a reparse point. What lies beneath it depends on where it points, which this
    /// milestone deliberately does not resolve, so no location below it is queried. The boundary is
    /// recorded rather than crossed.
    /// </summary>
    BlockedByReparsePoint = 2,

    /// <summary>
    /// An ancestor could not be checked, so whether anything exists below it is unknown. The branch is
    /// not queried further, and its descendants are never defaulted to absent.
    /// </summary>
    BlockedByFailedCheck = 3,

    /// <summary>
    /// A name component of the mapping cannot be queried without being reinterpreted — it contains a
    /// character ordinary Windows names cannot hold, it stands for a reserved device, or it ends with
    /// a space or a period. Querying a normalised spelling of it would describe a different object
    /// than the mapping names, so the location is not queried at all.
    /// </summary>
    AmbiguousName = 4,

    /// <summary>
    /// The relative path cannot be composed into a target location without changing the mapping —
    /// for example it is rooted, it is empty, or it walks out of the target with <c>..</c>. No path is
    /// built from it and nothing is queried.
    /// </summary>
    RelativePathNotQueryable = 5,

    /// <summary>
    /// The target is not a usable directory, so no mapped location under it was queried. The target's
    /// own rejection is reported by the target probe's blocking issue, not restated here.
    /// </summary>
    TargetRootUnusable = 6,

    /// <summary>
    /// The check was cancelled before this location was reached. A location that was never reached is
    /// not absent, and this reason exists so it can never be recorded as though it were.
    /// </summary>
    Cancelled = 7,
}

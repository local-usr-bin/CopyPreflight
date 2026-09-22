namespace CopyPreflight.Windows;

/// <summary>
/// What was established about the object, if any, that already exists at one mapped destination
/// location — or why nothing was established.
/// </summary>
/// <remarks>
/// <para>
/// One observation belongs to one source object, identified by its relative path, and describes the
/// location that object's mapping points at. It is a <b>fact</b>: it carries no
/// <see cref="CopyPreflight.Core.Severity"/>, no title and no recommendation, and it never triggers
/// a change. Deciding what "a file already exists there" or "the check did not answer" means for a
/// migration belongs to the rule layer.
/// </para>
/// <para>
/// The mapping identity is the source's own relative path, kept exactly as observed. It is
/// never trimmed, never repaired and never used to query a normalised spelling of a name: the
/// location that was examined is the location the mapping expresses, or the observation says that
/// none was examined.
/// </para>
/// <para>
/// <see cref="MappedTargetPath"/> is the composed mapping location —
/// <c>Path.Join(targetDirectory, relativePath)</c> — and never the internal form a query happened to
/// use. A path that has to be handed to Windows in another namespace is an implementation detail of
/// the query, not a property of the migration, and it is never written back here.
/// </para>
/// <para>
/// Instances are created through the factories below, so the facts each kind may carry — and the
/// facts it may not — are stated in one place.
/// </para>
/// </remarks>
public sealed record ExistingObjectObservation
{
    private ExistingObjectObservation(
        string sourceRelativePath,
        ExistingObjectObservationKind kind,
        TargetObjectPresence? presence,
        string? mappedTargetPath,
        ExistingObjectGapReason? gapReason,
        string? blockingAncestorRelativePath,
        int? nativeErrorCode,
        string diagnostic)
    {
        SourceRelativePath = sourceRelativePath;
        Kind = kind;
        Presence = presence;
        MappedTargetPath = mappedTargetPath;
        GapReason = gapReason;
        BlockingAncestorRelativePath = blockingAncestorRelativePath;
        NativeErrorCode = nativeErrorCode;
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// Relative path of the source object this observation belongs to, exactly as the scanner
    /// observed it. It is the mapping identity, so it is also the key that relates this observation
    /// to its source entry.
    /// </summary>
    public string SourceRelativePath { get; }

    /// <summary>How the state of the mapped location came to be known, or why it did not.</summary>
    public ExistingObjectObservationKind Kind { get; }

    /// <summary>
    /// What Windows reported at the mapped location. Non-null exactly when the location was queried
    /// and an object was found; <see langword="null"/> for every other kind, because there is no
    /// object to describe.
    /// </summary>
    public TargetObjectPresence? Presence { get; }

    /// <summary>
    /// The composed mapping location this observation is about —
    /// <c>Path.Join(targetDirectory, relativePath)</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when the relative path could not be composed into a location at all,
    /// because composing it would have changed what the mapping means. It is always the mapping
    /// location and never the internal form used to query it.
    /// </remarks>
    public string? MappedTargetPath { get; }

    /// <summary>
    /// Why no direct observation of this location exists. Non-null for
    /// <see cref="ExistingObjectObservationKind.InferredMissing"/> and
    /// <see cref="ExistingObjectObservationKind.NotQueried"/>; <see langword="null"/> when a query
    /// was actually made.
    /// </summary>
    public ExistingObjectGapReason? GapReason { get; }

    /// <summary>
    /// Relative path of the ancestor that caused this location to be inferred absent or left
    /// unqueried; <see langword="null"/> when no single ancestor explains it.
    /// </summary>
    /// <remarks>
    /// This is what keeps a blocked branch explainable without repeating its root cause on every
    /// descendant: the relation is recorded on each affected object, and the cause is reported once,
    /// at the ancestor that produced it.
    /// </remarks>
    public string? BlockingAncestorRelativePath { get; }

    /// <summary>
    /// The native error this observation rests on, or <see langword="null"/> when it rests on none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a real observed error code is ever stored here — never a fabricated or defaulted one. For
    /// an observation of a location that was queried itself, this is the code its own call reported:
    /// <c>ERROR_FILE_NOT_FOUND</c> for an established absence, or the code a failed call reported.
    /// For a location that was <i>not</i> queried because an ancestor stopped the branch, it is the
    /// code the failing ancestor call reported, and
    /// <see cref="BlockingAncestorRelativePath"/> names that ancestor. A location that was never
    /// queried and has no failing ancestor carries no code at all.
    /// </para>
    /// </remarks>
    public int? NativeErrorCode { get; }

    /// <summary>
    /// Short, deterministic description of what was established or what prevented it. It explains the
    /// gap; it is never a substitute for an observation, and it is never phrased as advice.
    /// </summary>
    public string Diagnostic { get; }

    /// <summary>
    /// Whether this observation leaves its location unexamined, so that no claim at all may be made
    /// about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single definition of "coverage gap" used by both the probe result and the rule
    /// layer, so a report and the facts behind it cannot disagree about which locations were covered.
    /// </para>
    /// <para>
    /// A branch stopped by an ancestor that already holds a regular file is deliberately <b>not</b> a
    /// gap. Nothing can exist below a file, the ancestor's own structural conflict is reported where
    /// it occurs, and counting every descendant as an unchecked location would bury that one real
    /// problem under a count of thousands.
    /// </para>
    /// </remarks>
    public bool IsCoverageGap => Kind switch
    {
        ExistingObjectObservationKind.CheckFailed => true,
        ExistingObjectObservationKind.NotQueried =>
            GapReason != ExistingObjectGapReason.BlockedByExistingObject,
        _ => false,
    };

    /// <summary>Records that the mapped location was queried and holds an object.</summary>
    /// <param name="sourceRelativePath">Relative path of the source object this mapping belongs to.</param>
    /// <param name="mappedTargetPath">The composed mapping location that was queried.</param>
    /// <param name="presence">What Windows reported there.</param>
    /// <param name="diagnostic">Short, deterministic description of the observation.</param>
    /// <exception cref="ArgumentException">A text argument is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="presence"/> is not a known value.</exception>
    public static ExistingObjectObservation Observed(
        string sourceRelativePath,
        string mappedTargetPath,
        TargetObjectPresence presence,
        string diagnostic)
    {
        RequirePresence(presence);

        return new ExistingObjectObservation(
            RequireText(sourceRelativePath, nameof(sourceRelativePath)),
            ExistingObjectObservationKind.Observed,
            presence,
            RequireText(mappedTargetPath, nameof(mappedTargetPath)),
            gapReason: null,
            blockingAncestorRelativePath: null,
            nativeErrorCode: null,
            RequireText(diagnostic, nameof(diagnostic)));
    }

    /// <summary>Records that the mapped location was queried and nothing is currently there.</summary>
    /// <param name="sourceRelativePath">Relative path of the source object this mapping belongs to.</param>
    /// <param name="mappedTargetPath">The composed mapping location that was queried.</param>
    /// <param name="nativeErrorCode">
    /// The code of the call that established the absence, or <see langword="null"/> when that call
    /// reported no error code of its own. It is the evidence for this observation, so it is a real
    /// observed code rather than a default.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of the observation.</param>
    /// <exception cref="ArgumentException">A text argument is null, empty or whitespace.</exception>
    public static ExistingObjectObservation ObservedMissing(
        string sourceRelativePath,
        string mappedTargetPath,
        int? nativeErrorCode,
        string diagnostic)
        => new(
            RequireText(sourceRelativePath, nameof(sourceRelativePath)),
            ExistingObjectObservationKind.ObservedMissing,
            presence: null,
            RequireText(mappedTargetPath, nameof(mappedTargetPath)),
            gapReason: null,
            blockingAncestorRelativePath: null,
            nativeErrorCode,
            RequireText(diagnostic, nameof(diagnostic)));

    /// <summary>Records that nothing can exist at the mapped location because an ancestor is absent.</summary>
    /// <param name="sourceRelativePath">Relative path of the source object this mapping belongs to.</param>
    /// <param name="mappedTargetPath">The composed mapping location, which was not queried.</param>
    /// <param name="missingAncestorRelativePath">Relative path of the ancestor established as absent.</param>
    /// <param name="nativeErrorCode">
    /// The code the ancestor's call reported when it established the absence, or <see langword="null"/>
    /// when it reported none. It is attributed to the ancestor named by
    /// <paramref name="missingAncestorRelativePath"/>, not to this location, which was never queried.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of the derivation.</param>
    /// <exception cref="ArgumentException">A text argument is null, empty or whitespace.</exception>
    public static ExistingObjectObservation InferredMissing(
        string sourceRelativePath,
        string mappedTargetPath,
        string missingAncestorRelativePath,
        int? nativeErrorCode,
        string diagnostic)
        => new(
            RequireText(sourceRelativePath, nameof(sourceRelativePath)),
            ExistingObjectObservationKind.InferredMissing,
            presence: null,
            RequireText(mappedTargetPath, nameof(mappedTargetPath)),
            ExistingObjectGapReason.MissingAncestor,
            RequireText(missingAncestorRelativePath, nameof(missingAncestorRelativePath)),
            nativeErrorCode,
            RequireText(diagnostic, nameof(diagnostic)));

    /// <summary>Records that the query about the mapped location did not answer.</summary>
    /// <param name="sourceRelativePath">Relative path of the source object this mapping belongs to.</param>
    /// <param name="mappedTargetPath">The composed mapping location that was queried.</param>
    /// <param name="nativeErrorCode">
    /// The code the failed call reported, or <see langword="null"/> when it reported none.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of the failure.</param>
    /// <exception cref="ArgumentException">A text argument is null, empty or whitespace.</exception>
    public static ExistingObjectObservation CheckFailed(
        string sourceRelativePath,
        string mappedTargetPath,
        int? nativeErrorCode,
        string diagnostic)
        => new(
            RequireText(sourceRelativePath, nameof(sourceRelativePath)),
            ExistingObjectObservationKind.CheckFailed,
            presence: null,
            RequireText(mappedTargetPath, nameof(mappedTargetPath)),
            gapReason: null,
            blockingAncestorRelativePath: null,
            nativeErrorCode,
            RequireText(diagnostic, nameof(diagnostic)));

    /// <summary>Records that the mapped location was not queried, and why.</summary>
    /// <param name="sourceRelativePath">Relative path of the source object this mapping belongs to.</param>
    /// <param name="gapReason">Why this location is not covered by an observation.</param>
    /// <param name="blockingAncestorRelativePath">
    /// Relative path of the ancestor responsible, when one explains the gap; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <param name="nativeErrorCode">
    /// The code a failing ancestor call reported, when a failed ancestor explains the gap; otherwise
    /// <see langword="null"/>. It is never a code for a query this location did not make.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of the gap.</param>
    /// <param name="mappedTargetPath">
    /// The composed mapping location, or <see langword="null"/> when the relative path could not be
    /// composed without changing the mapping.
    /// </param>
    /// <exception cref="ArgumentException">A text argument is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="gapReason"/> is not a known reason, or is
    /// <see cref="ExistingObjectGapReason.MissingAncestor"/>, which describes an inference rather
    /// than an unqueried location.
    /// </exception>
    public static ExistingObjectObservation NotQueried(
        string sourceRelativePath,
        ExistingObjectGapReason gapReason,
        string? blockingAncestorRelativePath,
        int? nativeErrorCode,
        string diagnostic,
        string? mappedTargetPath = null)
    {
        if (gapReason == ExistingObjectGapReason.MissingAncestor)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gapReason),
                gapReason,
                "An absent ancestor makes the location inferred absent, not merely unqueried; "
                + "use InferredMissing instead.");
        }

        if (!Enum.IsDefined(gapReason))
        {
            throw new ArgumentOutOfRangeException(nameof(gapReason), gapReason, "Unknown gap reason.");
        }

        return new ExistingObjectObservation(
            RequireText(sourceRelativePath, nameof(sourceRelativePath)),
            ExistingObjectObservationKind.NotQueried,
            presence: null,
            mappedTargetPath is null ? null : RequireText(mappedTargetPath, nameof(mappedTargetPath)),
            gapReason,
            blockingAncestorRelativePath is null
                ? null
                : RequireText(blockingAncestorRelativePath, nameof(blockingAncestorRelativePath)),
            nativeErrorCode,
            RequireText(diagnostic, nameof(diagnostic)));
    }

    private static void RequirePresence(TargetObjectPresence presence)
    {
        if (!Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "Unknown presence.");
        }
    }

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Observation text must not be null, empty or whitespace.",
                parameterName)
            : value;
}

namespace CopyPreflight.Windows;

/// <summary>
/// What was established about the target root itself — the directory the whole mapping hangs from.
/// </summary>
/// <remarks>
/// <para>
/// The target root is part of the mapping: the source root maps onto it. It is not a source entry, so
/// it has no relative path and cannot be described by an
/// <see cref="ExistingObjectObservation"/>, but its own kind still decides whether anything below it
/// can be examined at all — a file or a reparse point at the root stops every mapped location, and a
/// reparse point there is a fact the user has to be told about.
/// </para>
/// <para>
/// The probe is not the authority on whether the target can be used. That was settled earlier, from
/// positive evidence, by the target capability probe. This observation only says what the root's own
/// attribute query reported, and <see cref="TargetRootObservationKind.NotAnswered"/> is a normal
/// outcome rather than a failure: Microsoft documents that the attribute query does not answer for a
/// network share root, and such a target must still be usable.
/// </para>
/// </remarks>
public sealed record TargetRootObservation
{
    private TargetRootObservation(
        TargetRootObservationKind kind,
        int? nativeErrorCode,
        string diagnostic)
    {
        Kind = kind;
        NativeErrorCode = nativeErrorCode;
        Diagnostic = diagnostic;
    }

    /// <summary>What the root's own attribute query established.</summary>
    public TargetRootObservationKind Kind { get; }

    /// <summary>
    /// The native error the root's attribute query reported, when it did not answer; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public int? NativeErrorCode { get; }

    /// <summary>Short, deterministic description of what was established about the root.</summary>
    public string Diagnostic { get; }

    /// <summary>Records that the root was not queried, because the target had already been rejected.</summary>
    /// <param name="diagnostic">Short, deterministic description of why no query was made.</param>
    /// <exception cref="ArgumentException"><paramref name="diagnostic"/> is null, empty or whitespace.</exception>
    public static TargetRootObservation NotAttempted(string diagnostic)
        => new(TargetRootObservationKind.NotAttempted, nativeErrorCode: null, RequireText(diagnostic));

    /// <summary>Records what the root's attribute query reported.</summary>
    /// <param name="kind">
    /// <see cref="TargetRootObservationKind.Directory"/>,
    /// <see cref="TargetRootObservationKind.File"/> or
    /// <see cref="TargetRootObservationKind.ReparsePoint"/>.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of the observation.</param>
    /// <exception cref="ArgumentException"><paramref name="diagnostic"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not an answered kind.</exception>
    public static TargetRootObservation Answered(TargetRootObservationKind kind, string diagnostic)
    {
        if (kind is not (TargetRootObservationKind.Directory
            or TargetRootObservationKind.File
            or TargetRootObservationKind.ReparsePoint))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "An answered root observation must report a directory, a file or a reparse point.");
        }

        return new TargetRootObservation(kind, nativeErrorCode: null, RequireText(diagnostic));
    }

    /// <summary>Records that the root's attribute query did not answer.</summary>
    /// <param name="nativeErrorCode">The code the call reported.</param>
    /// <param name="diagnostic">Short, deterministic description of the failure.</param>
    /// <exception cref="ArgumentException"><paramref name="diagnostic"/> is null, empty or whitespace.</exception>
    public static TargetRootObservation NotAnswered(int nativeErrorCode, string diagnostic)
        => new(TargetRootObservationKind.NotAnswered, nativeErrorCode, RequireText(diagnostic));

    private static string RequireText(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Root observation text must not be null, empty or whitespace.",
                nameof(value))
            : value;
}

/// <summary>
/// What the target root's own attribute query established.
/// </summary>
/// <remarks>
/// <see cref="NotAttempted"/> and <see cref="NotAnswered"/> are kept apart because they are different
/// facts: the first means no question was asked, the second means the question was asked and Windows
/// said nothing — which, for a network share root, is documented and expected. Neither means the
/// target is unavailable.
/// </remarks>
public enum TargetRootObservationKind
{
    /// <summary>No query was made, because the target had already been rejected as a destination.</summary>
    NotAttempted = 0,

    /// <summary>The root is an ordinary directory, so the mapped locations below it can be examined.</summary>
    Directory = 1,

    /// <summary>The root is an ordinary file, so it cannot be the destination root of a migration.</summary>
    File = 2,

    /// <summary>The root carries the reparse point attribute. It is not classified and not followed.</summary>
    ReparsePoint = 3,

    /// <summary>
    /// The attribute query did not answer. This is not evidence that the target is unusable, and the
    /// mapped locations below it are still examined one by one.
    /// </summary>
    NotAnswered = 4,
}

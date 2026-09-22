namespace CopyPreflight.Windows;

/// <summary>
/// A recoverable failure recorded while probing a target directory.
/// </summary>
/// <remarks>
/// <para>
/// A probe issue records a <b>fact about a failed observation</b>, not a verdict. It carries no
/// severity, no title and no recommendation: deciding what "the file system of this volume could
/// not be described" means for a given migration belongs to the rule layer. For the same reason it
/// is kept separate from <see cref="CopyPreflight.Core.Finding"/>.
/// </para>
/// <para>
/// An issue never substitutes for a capability value. When a query fails, the corresponding
/// capability stays <see cref="CopyPreflight.Core.CapabilityValue{T}.Unknown"/>; the issue only
/// explains why.
/// </para>
/// <para>
/// No exception object is retained. Only the reported native error code and a short, deterministic
/// description are kept, so a probe result stays a plain, inspectable value that can be compared
/// between runs.
/// </para>
/// </remarks>
public sealed record TargetProbeIssue
{
    /// <summary>Creates a probe issue.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="affectedPath">
    /// The path the failing operation was applied to: the normalized target directory path for
    /// target-scoped operations, and the volume root path for volume-scoped ones. It is the path
    /// actually handed to the operation, not a re-derived one.
    /// </param>
    /// <param name="operation">
    /// The operation that failed, named after the Win32 entry point where one was involved, for
    /// example <c>GetVolumeInformationW</c>. This lets an issue be matched against Microsoft's
    /// documentation without knowing how kinds map onto APIs.
    /// </param>
    /// <param name="nativeErrorCode">
    /// The error Windows reported, or <see langword="null"/> when the operation reported no error
    /// of its own. It is never a fabricated or defaulted code.
    /// </param>
    /// <param name="diagnostic">Short, deterministic description of what could not be observed.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="affectedPath"/>, <paramref name="operation"/> or <paramref name="diagnostic"/>
    /// is null, empty or whitespace.
    /// </exception>
    public TargetProbeIssue(
        TargetProbeIssueKind kind,
        string affectedPath,
        string operation,
        int? nativeErrorCode,
        string diagnostic)
    {
        Kind = kind;
        AffectedPath = RequireText(affectedPath, nameof(affectedPath));
        Operation = RequireText(operation, nameof(operation));
        NativeErrorCode = nativeErrorCode;
        Diagnostic = RequireText(diagnostic, nameof(diagnostic));
    }

    /// <summary>What went wrong.</summary>
    public TargetProbeIssueKind Kind { get; }

    /// <summary>
    /// The path the failing operation was applied to. For target-scoped operations this is the
    /// normalized target directory path; for volume-scoped operations it is the volume root path.
    /// </summary>
    public string AffectedPath { get; }

    /// <summary>The operation that failed, named after the Win32 entry point where there was one.</summary>
    public string Operation { get; }

    /// <summary>
    /// The error Windows reported, or <see langword="null"/> when the operation reported no error of
    /// its own. This is the evidence behind the issue, preserved rather than paraphrased.
    /// </summary>
    public int? NativeErrorCode { get; }

    /// <summary>
    /// Short description of what could not be observed. It explains the gap; it is never a
    /// substitute for an observation, and it is never phrased as advice.
    /// </summary>
    public string Diagnostic { get; }

    private static string RequireText(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "Probe issue text must not be null, empty or whitespace.",
                parameterName)
            : value;
}

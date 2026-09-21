namespace CopyPreflight.Core;

/// <summary>
/// What went wrong while the scanner was observing the source tree.
/// </summary>
/// <remarks>
/// The kinds are deliberately coarse. They separate "I could not list this directory" from "I
/// could not establish what this individual object is", which are different facts about the
/// completeness of the inventory. CopyPreflight records them because it must never silently ignore
/// something it failed to look at.
/// </remarks>
public enum SourceScanIssueKind
{
    /// <summary>
    /// The children of a directory could not be enumerated, so its contents are unknown.
    /// </summary>
    CannotEnumerateDirectory = 0,

    /// <summary>
    /// An object was observed inside a listed directory, but the basic metadata needed to describe
    /// it could not be read, so it is not part of the inventory.
    /// </summary>
    CannotReadEntryMetadata = 1,
}

/// <summary>
/// A recoverable failure the scanner hit while observing the source tree.
/// </summary>
/// <remarks>
/// <para>
/// A scan issue records an <b>observation failure</b>, not a verdict. It carries no
/// <see cref="Severity"/> and no recommendation: deciding whether "a directory could not be
/// enumerated" is an Error, a Warning or merely informational for a given migration belongs to the
/// rule layer. For the same reason this type is kept separate from <see cref="Finding"/>, which
/// represents a conclusion rather than a fact.
/// </para>
/// <para>
/// The observed exception is described, never retained: only its type name, its
/// <see cref="Exception.HResult"/> and a short diagnostic are stored, so a scan result stays a
/// plain, inspectable value.
/// </para>
/// </remarks>
public sealed record SourceScanIssue
{
    /// <summary>Creates a scan issue without an underlying exception.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="relativePath">
    /// Path of the affected object relative to the source root, as far as it could be established.
    /// <see langword="null"/> when the failure concerns the source root itself, which is not an
    /// entry and therefore has no relative path.
    /// </param>
    /// <param name="exceptionTypeName">
    /// Full name of the observed exception type, or <see langword="null"/> when the failure was
    /// detected without one.
    /// </param>
    /// <param name="hResult">The observed exception's <see cref="Exception.HResult"/>, if any.</param>
    /// <param name="diagnostic">Short description of the failure.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="diagnostic"/> is null, empty or whitespace, or a supplied
    /// <paramref name="exceptionTypeName"/> is null, empty or whitespace.
    /// </exception>
    public SourceScanIssue(
        SourceScanIssueKind kind,
        string? relativePath,
        string? exceptionTypeName,
        int? hResult,
        string diagnostic)
    {
        if (kind is not (SourceScanIssueKind.CannotEnumerateDirectory or SourceScanIssueKind.CannotReadEntryMetadata))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown scan issue kind.");
        }

        Kind = kind;
        RelativePath = relativePath is null ? null : RequireText(relativePath, nameof(relativePath));
        ExceptionTypeName = exceptionTypeName is null ? null : RequireText(exceptionTypeName, nameof(exceptionTypeName));
        HResult = hResult;
        Diagnostic = RequireText(diagnostic, nameof(diagnostic));
    }

    /// <summary>What went wrong.</summary>
    public SourceScanIssueKind Kind { get; }

    /// <summary>
    /// Path of the affected object relative to the source root, as far as it could be established;
    /// <see langword="null"/> for a failure concerning the source root itself.
    /// </summary>
    public string? RelativePath { get; }

    /// <summary>Full name of the observed exception type, or <see langword="null"/>.</summary>
    public string? ExceptionTypeName { get; }

    /// <summary>The observed exception's <c>HResult</c>, or <see langword="null"/>.</summary>
    public int? HResult { get; }

    /// <summary>
    /// Short description of what failed, for diagnostics. It explains the gap in the inventory; it
    /// is never a substitute for an observation, and it is never phrased as advice.
    /// </summary>
    public string Diagnostic { get; }

    /// <summary>Creates a scan issue that describes the exception which caused the failure.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="relativePath">Relative path of the affected object, or <see langword="null"/>.</param>
    /// <param name="failure">The observed failure. It is described, not retained.</param>
    /// <returns>A scan issue carrying the failure's type name, <c>HResult</c> and message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
    public static SourceScanIssue FromException(
        SourceScanIssueKind kind,
        string? relativePath,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SourceScanIssue(
            kind,
            relativePath,
            failure.GetType().FullName,
            failure.HResult,
            failure.Message);
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Scan issue text must not be null, empty or whitespace.",
                parameterName);
        }

        return value;
    }
}

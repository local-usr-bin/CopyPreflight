namespace CopyPreflight.Windows;

/// <summary>
/// What went wrong while probing a target directory.
/// </summary>
/// <remarks>
/// <para>
/// The kinds separate facts that are easy to conflate but mean different things to a user: a target
/// path Windows will not accept as a name at all, a target path that is well formed but points at
/// nothing usable, and each individual volume query that could not be answered. They are deliberately
/// coarse and only as fine as one probe can honestly distinguish.
/// </para>
/// <para>
/// This is <b>not</b> a severity scale. Deciding whether an unavailable target is an Error or merely
/// informational belongs to the rule layer, which is why no kind carries a
/// <see cref="CopyPreflight.Core.Severity"/>.
/// </para>
/// </remarks>
public enum TargetProbeIssueKind
{
    /// <summary>
    /// Windows rejected the target path as a malformed name. The name's syntax is wrong, which is a
    /// different fact from a well-formed name that resolves to nothing.
    /// </summary>
    TargetPathInvalid = 0,

    /// <summary>
    /// The target path is a well-formed name, but no usable directory could be reached through it:
    /// nothing is there, it is not a directory, it could not be reached over the network, or access
    /// was denied.
    /// </summary>
    TargetPathUnavailable = 1,

    /// <summary>
    /// The volume a target path belongs to could not be determined, so its file system could not be
    /// described. No volume root is guessed in its place.
    /// </summary>
    VolumePathQueryFailed = 2,

    /// <summary>
    /// The space available to the calling user on the target could not be reported.
    /// </summary>
    FreeSpaceQueryFailed = 3,

    /// <summary>
    /// The volume was identified but Windows did not describe the file system on it, so neither its
    /// name nor its maximum path component length is known.
    /// </summary>
    VolumeInformationQueryFailed = 4,
}

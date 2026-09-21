namespace CopyPreflight.Core;

/// <summary>
/// The minimal, immutable set of target capabilities CopyPreflight currently reasons about.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a <see cref="CapabilityValue{T}"/>: a capability that could not be observed
/// with certainty stays unknown, and is never a guessed value.
/// </para>
/// <para>
/// This model is intentionally small. Capability flags such as hard-link, compression, encryption,
/// transaction or object-id support are <b>not</b> declared here: they are added only when a rule
/// or a probing implementation actually needs them.
/// </para>
/// <para>
/// The type is a pure data model: it performs no I/O and contains no probing logic.
/// </para>
/// </remarks>
public sealed record TargetCapabilities
{
    /// <summary>Creates the capability set of a migration target.</summary>
    /// <param name="fileSystemName">Identity of the target file system, or unknown.</param>
    /// <param name="availableFreeBytes">Free space on the target in bytes, or unknown.</param>
    /// <param name="maxPathComponentLength">
    /// Maximum length of a single path component (file or directory name) accepted by the target,
    /// or unknown.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="availableFreeBytes"/> is a known negative value, or
    /// <paramref name="maxPathComponentLength"/> is a known non-positive value. Such values are
    /// contradictory rather than merely unknown, so they are rejected instead of stored.
    /// </exception>
    public TargetCapabilities(
        CapabilityValue<FileSystemName> fileSystemName,
        CapabilityValue<long> availableFreeBytes,
        CapabilityValue<int> maxPathComponentLength)
    {
        if (availableFreeBytes.TryGetValue(out var freeBytes) && freeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableFreeBytes),
                freeBytes,
                "Free space cannot be negative when it is known; use Unknown() instead.");
        }

        if (maxPathComponentLength.TryGetValue(out var maxLength) && maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPathComponentLength),
                maxLength,
                "A maximum path component length must be positive when it is known; use Unknown() instead.");
        }

        FileSystemName = fileSystemName;
        AvailableFreeBytes = availableFreeBytes;
        MaxPathComponentLength = maxPathComponentLength;
    }

    /// <summary>Identity of the target file system, or unknown.</summary>
    public CapabilityValue<FileSystemName> FileSystemName { get; }

    /// <summary>Free space on the target in bytes, or unknown.</summary>
    public CapabilityValue<long> AvailableFreeBytes { get; }

    /// <summary>Maximum accepted length of a single path component, or unknown.</summary>
    public CapabilityValue<int> MaxPathComponentLength { get; }
}

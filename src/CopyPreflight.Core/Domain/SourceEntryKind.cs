namespace CopyPreflight.Core;

/// <summary>
/// Kind of a source entry observed by the scanner.
/// </summary>
/// <remarks>
/// v0.1 distinguishes only files and directories. Reparse points, symlinks and junctions are
/// observed as their underlying kind and classified by later milestones; they are deliberately not
/// part of this enum yet.
/// </remarks>
public enum SourceEntryKind
{
    /// <summary>A regular file.</summary>
    File = 0,

    /// <summary>A directory.</summary>
    Directory = 1,
}

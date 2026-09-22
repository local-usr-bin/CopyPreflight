using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Reports regular files that are too large for a FAT32 target to hold.
/// </summary>
/// <remarks>
/// <para>
/// The rule only runs when the target file system is <b>known</b> to be FAT32. An unknown file
/// system is never assumed to be FAT32, and a target that reports some other name is left alone.
/// The name is matched case-insensitively, because file system names are reported with inconsistent
/// casing in practice.
/// </para>
/// <para>
/// FAT32 addresses a file with a 32-bit byte count, so the largest file it can describe is
/// <see cref="uint.MaxValue"/> bytes — 4 GiB minus one byte — and that largest value is itself
/// representable. The comparison is therefore <c>&gt; uint.MaxValue</c>, not <c>&gt;=</c>, and the
/// exact boundary matters: a file of exactly 4,294,967,295 bytes is representable and is not
/// reported, while a file of 4,294,967,296 bytes is not and is. The round decimal number 4,000,000,000
/// is deliberately not used as an approximation of the limit.
/// </para>
/// <para>
/// Only regular files with a length the scanner actually attributed are considered. A reparse point
/// carries no attributed length at all, and no length is invented for one here, so this rule never
/// applies to it.
/// </para>
/// </remarks>
internal static class Fat32MaxFileSizeRule
{
    private const string Fat32Name = "FAT32";

    internal static IEnumerable<Finding> Evaluate(SourceScanResult source, TargetCapabilities capabilities)
    {
        if (!capabilities.FileSystemName.TryGetValue(out var fileSystemName)
            || !string.Equals(fileSystemName.Value, Fat32Name, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        foreach (var entry in source.Entries)
        {
            if (entry.IsReparsePoint || entry.Kind != SourceEntryKind.File)
            {
                continue;
            }

            // Guaranteed present for a regular file; an absent length is never treated as zero.
            if (entry.LengthInBytes is not { } length || length <= uint.MaxValue)
            {
                continue;
            }

            yield return CreateFinding(entry.RelativePath, length);
        }
    }

    private static Finding CreateFinding(string relativePath, long length)
        => new(
            WindowsPreflightFindingCodes.Fat32FileTooLarge,
            Severity.Error,
            "File is too large for a FAT32 target",
            $"The target uses the FAT32 file system, which cannot represent a single file larger than "
            + $"{uint.MaxValue} bytes (4 GiB minus 1 byte). This file is {length} bytes long.",
            "The file cannot be stored at its current size on this target, so it would not arrive "
            + "complete.",
            "If the target device and the way it is used allow it, exFAT or NTFS do not have this "
            + "per-file limit. If the target has to stay FAT32, splitting the file is something only "
            + "you can arrange. Reformatting a volume destroys the data already on it, so back that "
            + "data up before considering it.",
            sourceRelativePath: relativePath);
}

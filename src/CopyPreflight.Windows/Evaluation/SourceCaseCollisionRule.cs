using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Warns about sibling source objects whose names differ only in letter case.
/// </summary>
/// <remarks>
/// <para>
/// Entries are grouped by their parent relative directory together with their leaf name compared
/// <see cref="StringComparer.OrdinalIgnoreCase"/>. A group that turns out to hold two or more names
/// that are nevertheless distinct under <see cref="StringComparer.Ordinal"/> is one case-only
/// collision, and produces exactly <b>one</b> finding — not one per pair, which would multiply a
/// three-way collision into three findings for a single real problem. Grouping is done with a
/// dictionary keyed on the parent and the name, so nothing is compared pairwise.
/// </para>
/// <para>
/// The parent relative directory is part of the key and is compared <see cref="StringComparer.Ordinal"/>:
/// a collision is a collision between entries that the inventory places in the <i>same</i>
/// directory, so <c>dir1\A.txt</c> and <c>dir2\a.txt</c> are not in conflict — they would be created
/// in different directories. Parents that differ from each other only in letter case are a separate
/// question, answered by the collision reported for those two directory names themselves. Entries
/// that are identical under <see cref="StringComparer.Ordinal"/> share one name rather than
/// colliding with themselves.
/// </para>
/// <para>
/// The verdict is a <see cref="Severity.Warning"/> and never an Error, because nothing here has been
/// probed. Typical Windows file systems and the Win32 environment that most copy programs use are
/// case-insensitive, which is why the risk is worth reporting; but NTFS supports case sensitivity on
/// a per-directory basis, some targets and applications do distinguish such names, and CopyPreflight
/// has made no attempt to establish how this particular target behaves. Claiming a definite conflict
/// would assert more than the observed facts support.
/// </para>
/// <para>
/// Every entry takes part, whatever it is — file, directory or reparse point — because each one
/// occupies a name. The rule follows no reparse point, classifies none and reads no link target; it
/// only compares the names that were observed.
/// </para>
/// <para>
/// The rule reads nothing but the source inventory, so it is <b>source-only</b>: no target capability
/// is consulted and no destination path is composed, and it therefore still applies when the target
/// could not be used.
/// </para>
/// </remarks>
internal static class SourceCaseCollisionRule
{
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source)
    {
        foreach (var collision in FindCollisions(source))
        {
            yield return CreateFinding(collision.ParentDirectory, collision.Names);
        }
    }

    /// <summary>One parent directory plus the case-distinct names that collide inside it.</summary>
    private readonly record struct Collision(string ParentDirectory, IReadOnlyList<string> Names);

    private static IReadOnlyList<Collision> FindCollisions(SourceScanResult source)
    {
        var namesByParentAndName = new Dictionary<NameKey, List<string>>(NameKeyComparer.Instance);

        foreach (var entry in source.Entries)
        {
            var leafName = SourceRelativePathComponents.Leaf(entry.RelativePath);

            // A relative path consisting only of separators has no name to judge.
            if (leafName.Length == 0)
            {
                continue;
            }

            var key = new NameKey(SourceRelativePathComponents.ParentDirectory(entry.RelativePath), leafName);

            if (!namesByParentAndName.TryGetValue(key, out var names))
            {
                names = [];
                namesByParentAndName.Add(key, names);
            }

            names.Add(leafName);
        }

        var collisions = new List<Collision>();

        foreach (var (key, names) in namesByParentAndName)
        {
            // Sorted and deduplicated by Ordinal so that the finding text depends only on which names
            // were observed, never on the order the inventory happened to arrive in.
            var distinctNames = names
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            if (distinctNames.Length > 1)
            {
                collisions.Add(new Collision(key.ParentDirectory, distinctNames));
            }
        }

        return
        [
            .. collisions
                .OrderBy(collision => collision.ParentDirectory, StringComparer.Ordinal)
                .ThenBy(collision => collision.Names[0], StringComparer.Ordinal),
        ];
    }

    private static Finding CreateFinding(string parentDirectory, IReadOnlyList<string> names)
        => new(
            WindowsPreflightFindingCodes.SourceCaseCollisionRisk,
            Severity.Warning,
            "Names differ only by letter case",
            $"These sibling objects in the {DescribeLocation(parentDirectory)} have names that differ "
            + $"only in letter case: {DescribeNames(names)}.",
            "Typical Windows file systems and the Win32 environment that most copy programs use are "
            + "case-insensitive, so these names may map to the same object at the target and collide "
            + "during migration. Whether that happens — and which object wins, or whether the copy "
            + "stops — depends on the copy program and on the target's file system. Nothing is "
            + "certain here: NTFS can be made case-sensitive per directory, and some targets and "
            + "applications do distinguish names like these. CopyPreflight has not probed the "
            + "target's case sensitivity.",
            "If the target is case-insensitive, rename one of these objects in the source before "
            + "migrating. CopyPreflight never renames anything by itself.",
            sourceRelativePath: parentDirectory.Length == 0 ? null : parentDirectory);

    /// <summary>Where the colliding names sit, phrased for both a subdirectory and the source root.</summary>
    /// <remarks>
    /// The source root has no relative path of its own, so a collision among its direct children
    /// carries no <see cref="Finding.SourceRelativePath"/> and says so in words instead.
    /// </remarks>
    private static string DescribeLocation(string parentDirectory)
        => parentDirectory.Length == 0
            ? "source root directory"
            : $"source directory '{parentDirectory}'";

    private static string DescribeNames(IReadOnlyList<string> names)
        => string.Join(", ", names.Select(name => $"'{name}'"));

    /// <summary>
    /// The grouping key: one parent relative directory and one leaf name. The parent must be the
    /// same directory — the inventory's own spelling of it — while the leaf name is compared
    /// case-insensitively, so that every case variant of a name lands in the same group.
    /// </summary>
    private readonly record struct NameKey(string ParentDirectory, string LeafName);

    private sealed class NameKeyComparer : IEqualityComparer<NameKey>
    {
        internal static NameKeyComparer Instance { get; } = new();

        public bool Equals(NameKey x, NameKey y)
            => StringComparer.Ordinal.Equals(x.ParentDirectory, y.ParentDirectory)
            && StringComparer.OrdinalIgnoreCase.Equals(x.LeafName, y.LeafName);

        public int GetHashCode(NameKey obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.ParentDirectory),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.LeafName));
    }
}

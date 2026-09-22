using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Reports entries whose own name is longer than the target accepts.
/// </summary>
/// <remarks>
/// <para>
/// Only the <b>leaf</b> component of each entry's relative path is measured — the one name that this
/// entry itself would create at the target. For <c>a\b\c.txt</c> that is <c>c.txt</c>, and the
/// ancestors <c>a</c> and <c>b</c> are not re-reported here, because each of them has its own
/// <see cref="SourceEntry"/> and is measured there. Checking whole paths instead would make one
/// over-long directory name produce an Error on every one of its descendants, burying the single
/// real problem under thousands of duplicates.
/// </para>
/// <para>
/// The rule only runs when the target's maximum component length is <b>known</b>. An unknown limit is
/// never replaced by a guessed default such as 255: the limit differs between file systems, so a
/// guess could turn a legal name into a reported Error.
/// </para>
/// <para>
/// A name whose length equals the limit is legal and is not reported; only a genuinely longer name
/// is. Lengths are counted the way Windows counts them, in UTF-16 code units, and no Unicode
/// normalization is attempted — the name is judged exactly as it was observed.
/// </para>
/// </remarks>
internal static class TargetPathComponentRule
{
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source, TargetCapabilities capabilities)
    {
        if (!capabilities.MaxPathComponentLength.TryGetValue(out var maxLength))
        {
            yield break;
        }

        foreach (var entry in source.Entries)
        {
            var leafName = LeafComponent(entry.RelativePath);

            // A relative path that ends in a separator has no leaf name to judge. The scanner never
            // produces one, and inventing a component for it would report a name that does not exist.
            if (leafName.Length == 0 || leafName.Length <= maxLength)
            {
                continue;
            }

            yield return CreateFinding(entry.RelativePath, leafName, maxLength);
        }
    }

    /// <summary>The single name component this entry would create at the target.</summary>
    private static string LeafComponent(string relativePath)
        => Path.GetFileName(relativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static Finding CreateFinding(string relativePath, string leafName, int maxLength)
        => new(
            WindowsPreflightFindingCodes.TargetComponentTooLong,
            Severity.Error,
            "Name is longer than the target allows",
            $"The target accepts at most {maxLength} characters in a single file or directory name, "
            + $"and the name '{leafName}' is {leafName.Length} characters long.",
            "This name cannot be created at the target as it is, so this object would not arrive "
            + "under its current name.",
            "Shorten the name before copying. CopyPreflight never renames anything by itself.",
            sourceRelativePath: relativePath);
}

using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Warns about every observed object that carries the reparse point attribute.
/// </summary>
/// <remarks>
/// <para>
/// At this point CopyPreflight knows only that the object is a reparse point. It does <b>not</b> know
/// whether that means a symbolic link, a junction, a mount point or something else, so the finding
/// stays deliberately generic and never names a specific kind. It also never reads the link target,
/// follows it, or measures the object it points at.
/// </para>
/// <para>
/// The risk is real but conditional: copy programs disagree about reparse points, and which behaviour
/// occurs depends entirely on the program the user chooses. That is a Warning, not an Error.
/// </para>
/// <para>
/// The rule is source-only — it reads nothing from the target — so it still applies when the target
/// could not be used.
/// </para>
/// </remarks>
internal static class ReparsePointRule
{
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source)
    {
        foreach (var entry in source.Entries)
        {
            if (entry.IsReparsePoint)
            {
                yield return CreateFinding(entry.RelativePath);
            }
        }
    }

    private static Finding CreateFinding(string relativePath)
        => new(
            WindowsPreflightFindingCodes.ReparsePointPresent,
            Severity.Warning,
            "Special file system object (reparse point)",
            "This object carries the reparse point attribute. It may be a symbolic link, a junction, a "
            + "mount point or another kind of reparse object; CopyPreflight does not classify it and "
            + "does not read what it points at.",
            "Copy programs handle reparse points differently: some copy the object itself, some follow "
            + "it and copy what it points at, some skip it, and some behave in other ways. What "
            + "actually happens during your migration depends on the program you use.",
            "Confirm how the copy program you intend to use handles reparse points before migrating.",
            sourceRelativePath: relativePath);
}

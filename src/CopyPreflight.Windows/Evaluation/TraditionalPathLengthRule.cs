using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Warns when an entry's composed destination path is long enough to be a classic Windows path risk.
/// </summary>
/// <remarks>
/// <para>
/// The composed path follows the frozen mapping — the target directory is the destination root and
/// the source's relative structure is rebuilt underneath it — so every entry's destination is
/// <c>Path.Join(targetDirectory, relativePath)</c>. No normalisation is applied beyond that: the
/// path is judged as it would actually be formed.
/// </para>
/// <para>
/// Classic Windows path handling works with a <c>MAX_PATH</c> of 260 characters <i>including</i> the
/// terminating null, so a path of 260 characters or more already leaves no room for it. Classic
/// directory creation is stricter still: <c>CreateDirectory</c> must be able to append an 8.3 name
/// to the directory it is given, so an ordinary directory path may not exceed <c>MAX_PATH - 12</c>
/// (248) characters. Both are reported as a <see cref="Severity.Warning"/> and never as an Error,
/// because the outcome is not decided by the path alone.
/// </para>
/// <para>
/// What actually happens depends on the program that performs the copy and on the environment it
/// runs in. Long path support arrived with Windows 10 version 1607 and reaches only programs that
/// are themselves long-path aware, so this tool does not claim that a path beyond <c>MAX_PATH</c>
/// will fail, and it does not read the machine's long-path setting to claim that it will succeed:
/// the system setting alone cannot tell us what a future copy program will do.
/// </para>
/// <para>
/// Only the Win32 extended-length <b>file</b> namespace prefix <c>\\?\</c> (including
/// <c>\\?\UNC\…</c>) takes a destination path out of classic path handling, so only that prefix
/// suppresses the warning. The <c>\\.\</c> device namespace is <b>not</b> a long-path exemption and
/// is judged as the ordinary path it is. This is deliberately a minimal check of the documented
/// extended-length prefix, not a general NT path namespace parser: anything else is judged as the
/// ordinary path it is.
/// </para>
/// </remarks>
internal static class TraditionalPathLengthRule
{
    /// <summary>
    /// The classic Win32 <c>MAX_PATH</c>, counted in characters including the terminating null.
    /// </summary>
    private const int MaxPath = 260;

    /// <summary>
    /// The classic directory-creation limit, <c>MAX_PATH - 12</c>: classic <c>CreateDirectory</c>
    /// must still be able to append an 8.3 name to the path it is handed.
    /// </summary>
    private const int DirectoryPathLimit = MaxPath - 12;

    /// <summary>
    /// The Win32 extended-length file namespace prefix, the only prefix under which a path is
    /// passed on without classic path handling.
    /// </summary>
    private const string ExtendedLengthPrefix = @"\\?\";

    internal static IEnumerable<Finding> Evaluate(SourceScanResult source, string targetDirectoryPath)
    {
        foreach (var entry in source.Entries)
        {
            var destinationPath = Path.Join(targetDirectoryPath, entry.RelativePath);

            if (UsesExtendedLengthNamespace(destinationPath))
            {
                continue;
            }

            // An ordinary directory is recreated with CreateDirectory, whose documented limit is
            // shorter than the general classic path limit. A reparse point's shape says nothing
            // about how it would be recreated, so it is judged by the general limit instead of
            // being handed a directory-creation conclusion this milestone cannot honestly support.
            var isOrdinaryDirectory = !entry.IsReparsePoint && entry.Kind == SourceEntryKind.Directory;

            if (isOrdinaryDirectory)
            {
                if (destinationPath.Length > DirectoryPathLimit)
                {
                    yield return CreateDirectoryFinding(entry.RelativePath, destinationPath);
                }
            }
            else if (destinationPath.Length >= MaxPath)
            {
                yield return CreateFileFinding(entry.RelativePath, destinationPath);
            }
        }
    }

    /// <summary>
    /// Whether the path is rooted in the Win32 extended-length file namespace, which Windows passes
    /// on without applying classic <c>MAX_PATH</c> handling, so the length heuristics above do not
    /// describe it.
    /// </summary>
    private static bool UsesExtendedLengthNamespace(string path)
        => path.StartsWith(ExtendedLengthPrefix, StringComparison.OrdinalIgnoreCase);

    private static Finding CreateFileFinding(string relativePath, string destinationPath)
        => new(
            WindowsPreflightFindingCodes.TraditionalPathLengthRisk,
            Severity.Warning,
            "Destination path is beyond the classic Windows path limit",
            $"Following the source-to-target mapping, this object would be created as "
            + $"'{destinationPath}', which is {destinationPath.Length} characters long. Classic "
            + $"Windows path handling allows {MaxPath} characters including the terminating null, so "
            + "a path of this length leaves no room for one.",
            CompatibilityRiskImpact,
            CompatibilityRiskRecommendation,
            sourceRelativePath: relativePath);

    private static Finding CreateDirectoryFinding(string relativePath, string destinationPath)
        => new(
            WindowsPreflightFindingCodes.TraditionalPathLengthRisk,
            Severity.Warning,
            "Destination path is beyond the classic Windows path limit",
            $"Following the source-to-target mapping, this directory would be created as "
            + $"'{destinationPath}', which is {destinationPath.Length} characters long. Classic "
            + $"Windows directory creation allows at most {DirectoryPathLimit} characters, because "
            + "the directory path must still leave room to append an 8.3 file name.",
            CompatibilityRiskImpact,
            CompatibilityRiskRecommendation,
            sourceRelativePath: relativePath);

    private const string CompatibilityRiskImpact =
        "This is a compatibility risk rather than a certain failure. A copy program that is "
        + "long-path aware may handle the path — long path support arrived with Windows 10 "
        + "version 1607 — while a program that relies on classic path handling may fail or refuse "
        + "it. Which of those applies depends on the copy program you use and on its environment.";

    private const string CompatibilityRiskRecommendation =
        "If the copy program you intend to use cannot handle long paths, shorten the target "
        + "directory or the source structure before copying.";
}

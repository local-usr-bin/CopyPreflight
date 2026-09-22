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
/// terminating null, so a path of 260 characters or more already leaves no room for it. Such a path
/// is reported as a <see cref="Severity.Warning"/> and never as an Error, because the outcome is not
/// decided by the path alone.
/// </para>
/// <para>
/// What actually happens depends on the program that performs the copy and on the environment it
/// runs in. Long path support arrived with Windows 10 version 1607 and reaches only programs that
/// are themselves long-path aware, so this tool does not claim that a path beyond <c>MAX_PATH</c>
/// will fail, and it does not read the machine's long-path setting to claim that it will succeed:
/// the system setting alone cannot tell us what a future copy program will do.
/// </para>
/// <para>
/// When the destination path is already expressed in a namespace that does not go through classic
/// path handling, the classic limit does not describe it and the warning is not raised. This is
/// deliberately a minimal check of the documented Win32 namespace prefixes, not a general NT path
/// namespace parser: anything else is judged as the ordinary path it is.
/// </para>
/// </remarks>
internal static class TraditionalPathLengthRule
{
    /// <summary>
    /// The classic Win32 <c>MAX_PATH</c>, counted in characters including the terminating null.
    /// </summary>
    private const int MaxPath = 260;

    /// <summary>The Win32 extended-length prefix, which is passed on without classic path handling.</summary>
    private const string ExtendedLengthPrefix = @"\\?\";

    /// <summary>
    /// The Win32 device namespace prefix, which likewise does not go through classic path handling.
    /// </summary>
    private const string DeviceNamespacePrefix = @"\\.\";

    internal static IEnumerable<Finding> Evaluate(SourceScanResult source, string targetDirectoryPath)
    {
        foreach (var entry in source.Entries)
        {
            var destinationPath = Path.Join(targetDirectoryPath, entry.RelativePath);

            if (UsesNonClassicNamespace(destinationPath) || destinationPath.Length < MaxPath)
            {
                continue;
            }

            yield return CreateFinding(entry.RelativePath, destinationPath);
        }
    }

    /// <summary>
    /// Whether the path is rooted in a namespace Windows passes on without applying classic
    /// <c>MAX_PATH</c> handling, so the length heuristic above does not describe it.
    /// </summary>
    private static bool UsesNonClassicNamespace(string path)
        => path.StartsWith(ExtendedLengthPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(DeviceNamespacePrefix, StringComparison.OrdinalIgnoreCase);

    private static Finding CreateFinding(string relativePath, string destinationPath)
        => new(
            WindowsPreflightFindingCodes.TraditionalPathLengthRisk,
            Severity.Warning,
            "Destination path is beyond the classic Windows path limit",
            $"Following the source-to-target mapping, this object would be created as "
            + $"'{destinationPath}', which is {destinationPath.Length} characters long. Classic "
            + $"Windows path handling allows {MaxPath} characters including the terminating null, so "
            + "a path of this length leaves no room for one.",
            "This is a compatibility risk rather than a certain failure. A copy program that is "
            + "long-path aware may handle the path — long path support arrived with Windows 10 "
            + "version 1607 — while a program that relies on classic path handling may fail or refuse "
            + "it. Which of those applies depends on the copy program you use and on its environment.",
            "If the copy program you intend to use cannot handle long paths, shorten the target "
            + "directory or the source structure before copying.",
            sourceRelativePath: relativePath);
}

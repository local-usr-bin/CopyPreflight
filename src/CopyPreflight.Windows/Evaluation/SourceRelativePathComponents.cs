namespace CopyPreflight.Windows;

/// <summary>
/// Splits a source entry's relative path into the two parts the naming rules need: the leaf name it
/// would create at the target, and the relative directory that leaf would be created in.
/// </summary>
/// <remarks>
/// <para>
/// The split is done by scanning the string for separators directly, deliberately <b>not</b> with
/// <see cref="Path.GetDirectoryName(string)"/>. That helper is not a plain string operation: it
/// parses a volume specifier out of the path (so <c>a:.txt</c> yields the directory <c>a:</c> rather
/// than none) and it collapses repeated separators. A grouping key built from it could therefore
/// merge two genuinely different source directories, or invent a directory that does not exist —
/// and the names being examined here are exactly the ones that may contain such characters. Scanning
/// for the last separator keeps the answer a property of the observed string alone.
/// </para>
/// <para>
/// Both separators are recognised because a relative path is stored exactly as it was observed.
/// A trailing separator is ignored, so <c>a\b\</c> has the leaf <c>b</c>; a path with no separator
/// at all is a child of the source root.
/// </para>
/// </remarks>
internal static class SourceRelativePathComponents
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// The single name component this entry would create at the target: for <c>a\b\c.txt</c> that is
    /// <c>c.txt</c>. Empty when the path consists only of separators.
    /// </summary>
    internal static string Leaf(string relativePath)
    {
        var trimmed = TrimTrailingSeparators(relativePath);
        var lastSeparator = trimmed.LastIndexOfAny(Separators);

        return lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }

    /// <summary>
    /// The relative directory the leaf would be created in: for <c>a\b\c.txt</c> that is <c>a\b</c>.
    /// Empty when the entry is a child of the source root itself, which has no relative path of its
    /// own.
    /// </summary>
    internal static string ParentDirectory(string relativePath)
    {
        var trimmed = TrimTrailingSeparators(relativePath);
        var lastSeparator = trimmed.LastIndexOfAny(Separators);

        return lastSeparator >= 0 ? trimmed[..lastSeparator] : string.Empty;
    }

    private static string TrimTrailingSeparators(string relativePath)
        => relativePath.TrimEnd(Separators);
}

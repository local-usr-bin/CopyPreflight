using System.Text;
using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Reports source objects whose own name is not usable as an ordinary Windows file or directory
/// name.
/// </summary>
/// <remarks>
/// <para>
/// Only the <b>leaf</b> component of each entry's relative path is judged — the one name this entry
/// would create at the target. For <c>folder\sub\a.txt</c> that is <c>a.txt</c>; <c>folder</c> and
/// <c>sub</c> are not re-examined here, because each of them is a <see cref="SourceEntry"/> in its
/// own right and is judged there. Searching each whole relative path for a backslash or a colon
/// would be wrong for a different reason too: a backslash is the separator between components, so
/// it says nothing about any single name.
/// </para>
/// <para>
/// Three independent checks run on that leaf name, and a name that trips more than one of them is
/// reported for each of them:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Characters ordinary Windows names cannot contain</b> — <c>&lt; &gt; : " / \ | ? *</c> and the
/// control characters <c>U+0000</c>–<c>U+001F</c>. This is an Error. A colon is reserved for
/// alternate data streams, but this milestone does not analyse streams: as the name of a file or
/// directory it is simply one of the characters Windows does not accept.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Reserved device names</b> — <c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>, <c>COM1</c>–<c>COM9</c>,
/// <c>LPT1</c>–<c>LPT9</c> and the documented superscript forms <c>COM¹</c>/<c>COM²</c>/<c>COM³</c> and
/// <c>LPT¹</c>/<c>LPT²</c>/<c>LPT³</c>. This is an Error. Matching is case-insensitive, the check
/// applies to files and directories alike, and an extension does not release the name: the base
/// portion before the first period is what is matched, so <c>NUL.txt</c> and <c>NUL.tar.gz</c> are
/// both equivalent to <c>NUL</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>A trailing space or period</b> — a Warning, never an Error.
/// </description>
/// </item>
/// </list>
/// <para>
/// The rule reads nothing but the entry's own relative path, so it is <b>source-only</b>: it takes no
/// target capability and composes no destination path, and it therefore still applies when the target
/// could not be used. In particular the Win32 extended-length prefix <c>\\?\</c> does not exempt any
/// of these names. That prefix only takes a path out of classic <c>MAX_PATH</c> handling; it does not
/// make a reserved device name or an illegal character into something an ordinary user can rely on
/// when migrating files, and this milestone deliberately judges names by user-level Windows naming
/// semantics rather than by what a low-level namespace could be coaxed into creating.
/// </para>
/// <para>
/// Nothing here renames, repairs or rewrites anything: an illegal character is reported, never
/// replaced, and a trailing space is never silently trimmed. CopyPreflight only describes the
/// problem and leaves the change to the user.
/// </para>
/// </remarks>
internal static class WindowsNameCompatibilityRule
{
    /// <summary>
    /// The punctuation Windows does not accept in a file or directory name. The two separators are
    /// included because they are part of the documented set even though a leaf name produced by
    /// splitting on separators cannot itself contain one.
    /// </summary>
    private const string InvalidPunctuation = "<>:\"/\\|?*";

    /// <summary>
    /// The last character of the control range Windows rejects. The range starts at <c>U+0000</c>,
    /// which is why the test is <c>&lt;=</c> rather than a pair of bounds.
    /// </summary>
    private const char LastControlCharacter = '\u001F';

    /// <summary>The 8-bit ISO/IEC 8859-1 superscript digits Windows also recognises in device names.</summary>
    private static readonly char[] SuperscriptDigits = ['\u00B9', '\u00B2', '\u00B3'];

    private static readonly HashSet<string> ReservedDeviceNames = BuildReservedDeviceNames();

    internal static IEnumerable<Finding> Evaluate(SourceScanResult source)
    {
        foreach (var entry in source.Entries)
        {
            var leafName = SourceRelativePathComponents.Leaf(entry.RelativePath);

            // A relative path consisting only of separators has no name to judge. The scanner never
            // produces one, and inventing a name for it would report a name that does not exist.
            if (leafName.Length == 0)
            {
                continue;
            }

            if (InvalidCharactersIn(leafName) is { Count: > 0 } invalidCharacters)
            {
                yield return CreateInvalidCharacterFinding(entry.RelativePath, leafName, invalidCharacters);
            }

            if (ReservedDeviceNameIn(leafName) is { } deviceName)
            {
                yield return CreateReservedDeviceFinding(entry.RelativePath, leafName, deviceName);
            }

            if (leafName[^1] is ' ' or '.')
            {
                yield return CreateTrailingCharacterFinding(entry.RelativePath, leafName);
            }
        }
    }

    /// <summary>The reserved device name the leaf name stands for, or <see langword="null"/>.</summary>
    /// <remarks>
    /// Only the base portion — everything before the <i>first</i> period — is matched, because that
    /// is the part Windows compares, which is exactly why an extension cannot release the name.
    /// </remarks>
    private static string? ReservedDeviceNameIn(string leafName)
    {
        var firstPeriod = leafName.IndexOf('.');

        // A name that starts with a period has an empty base portion, so a leading period is not
        // treated as hiding a device name: ".temp" is an ordinary name.
        if (firstPeriod == 0)
        {
            return null;
        }

        var baseName = firstPeriod < 0 ? leafName : leafName[..firstPeriod];

        return ReservedDeviceNames.TryGetValue(baseName, out var deviceName) ? deviceName : null;
    }

    /// <summary>
    /// The distinct offending characters of the name, in ascending code-unit order so that the text
    /// of the finding depends only on the name.
    /// </summary>
    private static IReadOnlyList<char> InvalidCharactersIn(string leafName)
    {
        var found = new SortedSet<char>();

        foreach (var value in leafName)
        {
            if (IsInvalidNameCharacter(value))
            {
                found.Add(value);
            }
        }

        return [.. found];
    }

    private static bool IsInvalidNameCharacter(char value)
        => value <= LastControlCharacter || InvalidPunctuation.Contains(value, StringComparison.Ordinal);

    private static Finding CreateInvalidCharacterFinding(
        string relativePath,
        string leafName,
        IReadOnlyList<char> invalidCharacters)
        => new(
            WindowsPreflightFindingCodes.TargetNameInvalidCharacter,
            Severity.Error,
            "Name contains characters Windows does not allow",
            $"The name '{DescribeName(leafName)}' contains characters that an ordinary Windows file "
            + $"or directory name cannot contain: {DescribeCharacters(invalidCharacters)}. Windows "
            + "does not accept such a name through its normal file naming semantics.",
            "This object cannot be created at the target under its current name through ordinary "
            + "Windows file naming semantics, so it would not arrive under that name.",
            "Rename the object in the source before migrating, or handle it in whatever way suits "
            + "your actual environment. CopyPreflight never renames anything, replaces characters "
            + "or repairs a name by itself.",
            sourceRelativePath: relativePath);

    private static Finding CreateReservedDeviceFinding(
        string relativePath,
        string leafName,
        string deviceName)
        => new(
            WindowsPreflightFindingCodes.TargetNameReservedDevice,
            Severity.Error,
            "Name is a reserved Windows device name",
            $"The name '{DescribeName(leafName)}' is reserved by Windows: its base portion — the part "
            + $"before the first period — is the device name '{deviceName}'. Microsoft documents that "
            + $"a name such as '{deviceName}.txt' is still equivalent to '{deviceName}', so an "
            + "extension does not release the name.",
            "This object cannot be created at the target as a normal file or directory under its "
            + "current name, so it would not arrive under that name.",
            "Rename the object in the source before migrating. CopyPreflight never renames anything "
            + "by itself.",
            sourceRelativePath: relativePath);

    private static Finding CreateTrailingCharacterFinding(string relativePath, string leafName)
        => new(
            WindowsPreflightFindingCodes.TargetNameTrailingSpaceOrPeriod,
            Severity.Warning,
            "Name ends with a space or a period",
            $"The name '{DescribeName(leafName)}' ends with a "
            + (leafName[^1] == ' ' ? "space" : "period")
            + ". Microsoft advises against ending a file or directory name with a space or a period: "
            + "the underlying file system may support such a name, but the Windows shell and user "
            + "interface do not handle it properly. A leading period is not a problem — names such as "
            + "'.temp' are ordinary ones.",
            "This is a compatibility risk rather than a certain failure. The name may be rejected, "
            + "trimmed, or otherwise handled differently depending on the file system and on the copy "
            + "program you use, so the object may arrive under a different name — or not as a "
            + "separate object at all — rather than exactly as it is named in the source.",
            "Rename the object in the source before migrating if it has to keep exactly this name. "
            + "CopyPreflight never renames anything and never trims a name by itself.",
            sourceRelativePath: relativePath);

    /// <summary>Renders a name so that no control character can reach the display unannounced.</summary>
    private static string DescribeName(string name)
    {
        var description = new StringBuilder(name.Length);

        foreach (var value in name)
        {
            if (value <= LastControlCharacter)
            {
                description.Append($"U+{(int)value:X4}");
            }
            else
            {
                description.Append(value);
            }
        }

        return description.ToString();
    }

    private static string DescribeCharacters(IReadOnlyList<char> characters)
        => string.Join(", ", characters.Select(DescribeCharacter));

    private static string DescribeCharacter(char value)
        => value <= LastControlCharacter ? $"U+{(int)value:X4}" : $"'{value}'";

    /// <summary>
    /// The device names Microsoft documents as reserved, and only those. Names that merely look
    /// similar are not included: <c>COM0</c>, <c>COM10</c>, <c>LPT0</c> and <c>LPT10</c> are ordinary
    /// names, as are <c>CONSOLE</c>, <c>NULX</c> and <c>AUXILIARY</c>. This is a fixed list from the
    /// documentation, not a general DOS device name parser.
    /// </summary>
    private static HashSet<string> BuildReservedDeviceNames()
    {
        var deviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
        };

        // Building "COM1".."COM9" from characters keeps the spelling independent of the current
        // culture, which formatting an int would not be.
        for (var digit = '1'; digit <= '9'; digit++)
        {
            deviceNames.Add("COM" + digit);
            deviceNames.Add("LPT" + digit);
        }

        foreach (var superscript in SuperscriptDigits)
        {
            deviceNames.Add("COM" + superscript);
            deviceNames.Add("LPT" + superscript);
        }

        return deviceNames;
    }
}

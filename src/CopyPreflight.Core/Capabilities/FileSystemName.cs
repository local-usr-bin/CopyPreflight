namespace CopyPreflight.Core;

/// <summary>
/// Name of a file system as reported for a migration target, for example <c>"NTFS"</c>,
/// <c>"FAT32"</c>, <c>"exFAT"</c> or <c>"ReFS"</c>.
/// </summary>
/// <remarks>
/// <para>
/// File system names are <b>not</b> a closed set. Windows, network shares and future targets may
/// report names this tool has never heard of, so this type is an open, normalized string rather
/// than an enum listing only the well-known file systems.
/// </para>
/// <para>
/// Normalization is deliberately minimal: surrounding whitespace is trimmed, and nothing else is
/// changed. Comparison and hashing are case-insensitive (ordinal), because file system names are
/// reported with inconsistent casing in practice.
/// </para>
/// <para>
/// Whether a name is recognized is a question for rules, not for this type. An unrecognized but
/// observed name is still a known name.
/// </para>
/// </remarks>
public sealed class FileSystemName : IEquatable<FileSystemName>
{
    /// <summary>Creates a file system name from its reported text.</summary>
    /// <param name="value">The reported name. Must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, empty or consists only of whitespace.
    /// </exception>
    public FileSystemName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A file system name must not be null, empty or whitespace.",
                nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>The trimmed name, with its original casing preserved.</summary>
    public string Value { get; }

    /// <summary>Case-insensitive (ordinal) comparison of two file system names.</summary>
    public bool Equals(FileSystemName? other)
        => other is not null
            && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FileSystemName);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <summary>Case-insensitive value equality.</summary>
    public static bool operator ==(FileSystemName? left, FileSystemName? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Case-insensitive value inequality.</summary>
    public static bool operator !=(FileSystemName? left, FileSystemName? right) => !(left == right);

    /// <summary>Returns <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}

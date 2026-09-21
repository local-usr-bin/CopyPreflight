namespace CopyPreflight.Core;

/// <summary>
/// Stable identity of a rule and of the findings it produces, for example
/// <c>"FAT32_MAX_FILE_SIZE"</c>.
/// </summary>
/// <remarks>
/// <para>
/// A code is a short, machine-readable identifier that stays stable over time. It is deliberately
/// a free-form string rather than an enum: rules must be addable without editing a central
/// registry, and a code already carried by a finding must keep its meaning.
/// </para>
/// <para>
/// The only enforced invariant is that a code is never null, empty or whitespace. Codes are
/// compared ordinally and case-sensitively, and are not normalized, so rules must agree on a
/// single spelling for a given rule.
/// </para>
/// </remarks>
public sealed record FindingCode
{
    /// <summary>Creates a finding code from its text.</summary>
    /// <param name="value">The code text. Must not be null, empty or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, empty or consists only of whitespace.
    /// </exception>
    public FindingCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A finding code must not be null, empty or whitespace.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>The code text, exactly as supplied by the rule.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>, so diagnostics show the code itself.</summary>
    public override string ToString() => Value;
}

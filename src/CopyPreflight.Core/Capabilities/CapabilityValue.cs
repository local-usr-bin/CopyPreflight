using System.Diagnostics.CodeAnalysis;

namespace CopyPreflight.Core;

/// <summary>
/// A target capability value that is either <b>known with certainty</b> or <b>explicitly
/// unknown</b>.
/// </summary>
/// <remarks>
/// <para>
/// CopyPreflight must never guess. When a target capability cannot be observed with certainty it
/// stays <see cref="IsUnknown"/> — it must not be collapsed into <c>false</c>, <c>0</c>, an empty
/// string, or <c>null</c> whose meaning each caller has to re-invent.
/// </para>
/// <para>
/// The distinction is enforced by the API rather than by convention:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Value"/> throws for an unknown value instead of silently returning <c>default(T)</c>.</description></item>
/// <item><description><see cref="TryGetValue"/> makes the caller handle the unknown case before reading anything.</description></item>
/// <item><description>There is no implicit conversion to <typeparamref name="T"/>.</description></item>
/// </list>
/// <para>
/// Equal values are <b>not</b> equal states: <c>Known(false)</c> and <c>Known(0)</c> are known
/// values, and both differ from <see cref="Unknown"/>.
/// </para>
/// <para>
/// <c>default(CapabilityValue&lt;T&gt;)</c> is a valid unknown value, so "unknown" is also the safe
/// default of the type.
/// </para>
/// </remarks>
/// <typeparam name="T">Type of the capability value when it is known.</typeparam>
public readonly struct CapabilityValue<T> : IEquatable<CapabilityValue<T>>
{
    private readonly T _value;
    private readonly bool _isKnown;
    private readonly string? _diagnostic;

    private CapabilityValue(T value, bool isKnown, string? diagnostic)
    {
        _value = value;
        _isKnown = isKnown;
        _diagnostic = diagnostic;
    }

    /// <summary>Creates a capability value that is known with certainty.</summary>
    /// <param name="value">The observed value. Must not be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static CapabilityValue<T> Known(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(
                nameof(value),
                "A known capability value must not be null.");
        }

        return new CapabilityValue<T>(value, isKnown: true, diagnostic: null);
    }

    /// <summary>Creates a capability value that is explicitly unknown.</summary>
    /// <param name="diagnostic">
    /// Optional short note explaining why the value could not be determined. It never carries a
    /// guessed value.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="diagnostic"/> is supplied but empty or whitespace.
    /// </exception>
    public static CapabilityValue<T> Unknown(string? diagnostic = null)
    {
        if (diagnostic is not null && string.IsNullOrWhiteSpace(diagnostic))
        {
            throw new ArgumentException(
                "A diagnostic must not be empty or whitespace when it is supplied.",
                nameof(diagnostic));
        }

        return new CapabilityValue<T>(default!, isKnown: false, diagnostic: diagnostic);
    }

    /// <summary><see langword="true"/> when the value was observed with certainty.</summary>
    public bool IsKnown => _isKnown;

    /// <summary><see langword="true"/> when the value could not be determined and stays unknown.</summary>
    public bool IsUnknown => !_isKnown;

    /// <summary>
    /// Why the value is unknown, or <see langword="null"/> when the value is known or no note was
    /// supplied. A diagnostic explains the gap; it is never a substitute for a value.
    /// </summary>
    public string? Diagnostic => _isKnown ? null : _diagnostic;

    /// <summary>The known value.</summary>
    /// <exception cref="InvalidOperationException">
    /// The value is unknown. Unknown is a real state, not <c>default(T)</c>; check
    /// <see cref="IsKnown"/> or use <see cref="TryGetValue"/> first.
    /// </exception>
    public T Value => _isKnown
        ? _value
        : throw new InvalidOperationException(
            "The capability value is unknown, so there is no value to read. " +
            "Check IsKnown or use TryGetValue instead of assuming a default value.");

    /// <summary>Reads the value without ever falling back to <c>default(T)</c>.</summary>
    /// <param name="value">
    /// The known value when this method returns <see langword="true"/>; otherwise unspecified.
    /// </param>
    /// <returns><see langword="true"/> when the value is known.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _isKnown;
    }

    /// <summary>
    /// Compares state first, then value: two known values are equal when their values are equal,
    /// two unknown values are equal when their diagnostics are equal.
    /// </summary>
    public bool Equals(CapabilityValue<T> other)
        => _isKnown
            ? other._isKnown && EqualityComparer<T>.Default.Equals(_value, other._value)
            : !other._isKnown && string.Equals(_diagnostic, other._diagnostic, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CapabilityValue<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => _isKnown
            ? HashCode.Combine(true, _value)
            : HashCode.Combine(false, _diagnostic);

    /// <summary>Value equality. See <see cref="Equals(CapabilityValue{T})"/>.</summary>
    public static bool operator ==(CapabilityValue<T> left, CapabilityValue<T> right) => left.Equals(right);

    /// <summary>Value inequality. See <see cref="Equals(CapabilityValue{T})"/>.</summary>
    public static bool operator !=(CapabilityValue<T> left, CapabilityValue<T> right) => !left.Equals(right);

    /// <summary>Returns <c>Known(value)</c> or <c>Unknown</c>, for diagnostics and logs.</summary>
    public override string ToString()
        => _isKnown
            ? $"Known({_value})"
            : _diagnostic is null ? "Unknown" : $"Unknown({_diagnostic})";
}

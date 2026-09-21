using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Semantics of the known / unknown capability value.
/// </summary>
public class CapabilityValueTests
{
    [Fact]
    public void KnownFalseIsAKnownValueThatCarriesFalse()
    {
        var value = CapabilityValue<bool>.Known(false);

        Assert.True(value.IsKnown);
        Assert.False(value.IsUnknown);
        Assert.False(value.Value);
        Assert.True(value.TryGetValue(out var read));
        Assert.False(read);
    }

    [Fact]
    public void KnownZeroIsAKnownValueThatCarriesZero()
    {
        var value = CapabilityValue<long>.Known(0);

        Assert.True(value.IsKnown);
        Assert.Equal(0L, value.Value);
    }

    [Fact]
    public void KnownKeepsTheActualValue()
    {
        var value = CapabilityValue<string>.Known("NTFS");

        Assert.True(value.IsKnown);
        Assert.Equal("NTFS", value.Value);
    }

    [Fact]
    public void KnownEmptyStringIsStillKnown()
    {
        var value = CapabilityValue<string>.Known(string.Empty);

        Assert.True(value.IsKnown);
        Assert.Equal(string.Empty, value.Value);
    }

    [Fact]
    public void UnknownIsNotEquivalentToTheTypesDefaultValue()
    {
        Assert.NotEqual(CapabilityValue<bool>.Known(false), CapabilityValue<bool>.Unknown());
        Assert.NotEqual(CapabilityValue<int>.Known(0), CapabilityValue<int>.Unknown());
        Assert.NotEqual(CapabilityValue<string>.Known(string.Empty), CapabilityValue<string>.Unknown());
    }

    [Fact]
    public void ReadingTheValueOfAnUnknownThrowsInsteadOfReturningDefault()
    {
        var value = CapabilityValue<int>.Unknown();

        Assert.True(value.IsUnknown);
        Assert.Throws<InvalidOperationException>(() => { _ = value.Value; });
    }

    [Fact]
    public void TryGetValueReportsUnknownInsteadOfSilentlyReturningDefault()
    {
        var value = CapabilityValue<int>.Unknown();

        Assert.False(value.TryGetValue(out _));
    }

    [Fact]
    public void UnknownKeepsAnOptionalDiagnostic()
    {
        var unknown = CapabilityValue<string>.Unknown("the network target did not report a file system");
        var known = CapabilityValue<string>.Known("NTFS");

        Assert.Equal("the network target did not report a file system", unknown.Diagnostic);
        Assert.Null(known.Diagnostic);
    }

    [Fact]
    public void RejectsABlankDiagnostic()
    {
        Assert.Throws<ArgumentException>(() => { _ = CapabilityValue<string>.Unknown("   "); });
    }

    [Fact]
    public void RejectsAKnownNull()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = CapabilityValue<string>.Known(null!); });
    }

    [Fact]
    public void DefaultIsAnUnknownValue()
    {
        CapabilityValue<int> value = default;

        Assert.True(value.IsUnknown);
        Assert.Equal(CapabilityValue<int>.Unknown(), value);
    }

    [Fact]
    public void UnknownValuesDifferWhenTheirDiagnosticsDiffer()
    {
        Assert.Equal(CapabilityValue<int>.Unknown("timed out"), CapabilityValue<int>.Unknown("timed out"));
        Assert.NotEqual(CapabilityValue<int>.Unknown("timed out"), CapabilityValue<int>.Unknown("denied"));
        Assert.NotEqual(CapabilityValue<int>.Unknown("timed out"), CapabilityValue<int>.Unknown());
    }
}

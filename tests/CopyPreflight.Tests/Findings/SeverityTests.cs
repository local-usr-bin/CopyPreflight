using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Semantics of the frozen three-level severity scale.
/// </summary>
public class SeverityTests
{
    [Fact]
    public void DeclaresExactlyTheThreeFrozenLevels()
    {
        var names = Enum.GetNames<Severity>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Error", "Info", "Warning" }, names);
    }

    [Theory]
    [InlineData(Severity.Error)]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Info)]
    public void EachLevelIsStableAcrossRoundTrips(Severity severity)
    {
        Assert.True(Enum.IsDefined(severity));
        Assert.Equal(severity, Enum.Parse<Severity>(severity.ToString()));
    }

    [Fact]
    public void LevelsAreMutuallyDistinct()
    {
        var values = new[] { Severity.Error, Severity.Warning, Severity.Info };

        Assert.Equal(3, values.Distinct().Count());
    }
}

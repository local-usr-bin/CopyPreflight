using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Engineering-baseline smoke tests.
/// </summary>
/// <remarks>
/// These tests verify the test infrastructure itself, not product behavior: that the test runner
/// executes assertions, and that the project reference from CopyPreflight.Tests to
/// CopyPreflight.Core resolves. The project-reference check used the M0 placeholder type until M1
/// replaced it with a real domain type; the intent of the check is unchanged.
/// </remarks>
public class EngineeringBaselineSmokeTests
{
    [Fact]
    public void TestRunner_ExecutesAssertions()
    {
        // Intentionally trivial: proves the runner discovers and executes tests.
        Assert.Equal(2, 1 + 1);
    }

    [Fact]
    public void CoreProjectReference_ResolvesToExpectedAssembly()
    {
        var assembly = typeof(Finding).Assembly;

        Assert.Equal("CopyPreflight.Core", assembly.GetName().Name);
    }
}

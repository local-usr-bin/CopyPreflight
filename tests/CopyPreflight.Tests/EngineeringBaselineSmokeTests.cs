using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// M0 engineering-baseline smoke tests.
/// </summary>
/// <remarks>
/// These tests verify the test infrastructure itself, not product behavior:
/// that the test runner executes assertions, and that the project reference from
/// CopyPreflight.Tests to CopyPreflight.Core resolves. Product behavior tests
/// arrive with later milestones.
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
        var assembly = typeof(CoreBaselineMarker).Assembly;

        Assert.Equal(CoreBaselineMarker.AssemblyName, assembly.GetName().Name);
    }
}

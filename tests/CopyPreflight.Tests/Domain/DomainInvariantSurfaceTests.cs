using System.Reflection;
using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Guards the invariant surface of the domain model: the validators that live in constructors and
/// factories cannot be side-stepped from outside the assembly.
/// </summary>
/// <remarks>
/// A <c>with { Property = value }</c> expression, like an object initializer, can only target a
/// property that has an accessible setter; <c>init</c> accessors count. So if every public instance
/// property of a type has no setter at all, no initializer can reach it, and the only way to build an
/// instance remains the validating constructor or factory. The synthesized record clone only copies
/// an instance that is already valid, so it cannot introduce an invalid state either.
/// </remarks>
public class DomainInvariantSurfaceTests
{
    public static TheoryData<Type> ValidatedDomainTypes => new()
    {
        typeof(SourceEntry),
        typeof(TargetCapabilities),
        typeof(Finding),
        typeof(FindingCode),
        typeof(FileSystemName),
    };

    [Theory]
    [MemberData(nameof(ValidatedDomainTypes))]
    public void NoWithExpressionCanBypassAValidator(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.Null(property.SetMethod);
            Assert.False(property.CanWrite);
        }
    }

    [Fact]
    public void SourceEntryCanOnlyBeBuiltThroughItsFactories()
    {
        // No public constructor at all, so File / Directory / ReparsePoint are the only doors.
        Assert.Empty(typeof(SourceEntry).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}

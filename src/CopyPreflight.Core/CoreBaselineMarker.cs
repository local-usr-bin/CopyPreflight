namespace CopyPreflight.Core;

/// <summary>
/// M0 engineering-baseline placeholder.
/// </summary>
/// <remarks>
/// This type carries no product or business behavior. It exists only so that
/// dependent projects (CopyPreflight.App, CopyPreflight.Tests) have a stable
/// symbol to reference, which verifies that project references and the build/test
/// pipeline are correctly wired up. It is expected to be removed or superseded
/// once real Core APIs are introduced in a later milestone.
/// </remarks>
public static class CoreBaselineMarker
{
    /// <summary>
    /// The expected assembly simple name of the core library.
    /// Used by the M0 smoke test to confirm the referenced assembly resolves.
    /// </summary>
    public const string AssemblyName = "CopyPreflight.Core";
}

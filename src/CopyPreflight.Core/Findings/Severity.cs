namespace CopyPreflight.Core;

/// <summary>
/// Severity of a <see cref="Finding"/>, as frozen by <c>docs/PRODUCT_SPEC.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Severity expresses how certain the verdict is and what kind of impact it has; it is not a
/// generic low/medium/high ranking:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="Error"/> — based on facts that are currently known, migration will (or almost
/// certainly will) be blocked.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Warning"/> — a real migration or compatibility risk exists, but the outcome
/// depends on the copy program, the policy, the environment or runtime state.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Info"/> — the content itself can migrate, but something changed that the user
/// should know about (behavior, capability or metadata difference).
/// </description>
/// </item>
/// </list>
/// <para>
/// Exactly these three levels exist in v0.1. Levels such as Critical, Fatal, Success or Debug are
/// deliberately <b>not</b> defined and must not be added without an explicit product-scope change.
/// </para>
/// </remarks>
public enum Severity
{
    /// <summary>Migration is expected to be blocked based on currently known facts.</summary>
    Error = 0,

    /// <summary>
    /// A real migration or compatibility risk exists, but whether it becomes a failure depends on
    /// factors outside the known facts.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// The content can migrate, but there is a behavior, capability or metadata change worth
    /// knowing about.
    /// </summary>
    Info = 2,
}

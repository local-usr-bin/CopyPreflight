using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Warns when the observed source size is larger than the space Windows reported as available.
/// </summary>
/// <remarks>
/// <para>
/// The comparison uses <see cref="SourceScanResult.LogicalFileBytes"/>, which is the sum of the
/// lengths the scanner could honestly attribute to regular files. Reparse points contribute nothing
/// to it, because no length was attributed to them.
/// </para>
/// <para>
/// This can only ever be a <see cref="Severity.Warning"/>, and the reason is not caution but honesty:
/// the preflight check does not yet look at target collisions, does not know the overwrite or skip
/// policy the copy will use, does not analyse which destination files already exist, does not know
/// how reparse points will be copied, and cannot know how the file system will allocate sparse or
/// compressed data. Any of those can change what the copy actually needs, so an Error would claim
/// more certainty than the observed facts support.
/// </para>
/// <para>
/// The reverse case produces nothing at all. Source bytes that fit inside the reported free space do
/// not prove that the migration will fit, so no reassuring "space is sufficient" finding is invented:
/// only problems are reported, and only when they were actually observed.
/// </para>
/// <para>
/// Unknown free space means the comparison is not made, because there is nothing to compare against.
/// </para>
/// </remarks>
internal static class TargetFreeSpaceRule
{
    internal static IEnumerable<Finding> Evaluate(SourceScanResult source, TargetCapabilities capabilities)
    {
        if (!capabilities.AvailableFreeBytes.TryGetValue(out var availableFreeBytes)
            || source.LogicalFileBytes <= availableFreeBytes)
        {
            yield break;
        }

        yield return CreateFinding(source.LogicalFileBytes, availableFreeBytes);
    }

    private static Finding CreateFinding(long logicalFileBytes, long availableFreeBytes)
        => new(
            WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient,
            Severity.Warning,
            "Target may not have enough free space",
            $"The regular files observed in the source add up to {logicalFileBytes} logical bytes, "
            + $"while Windows reported {availableFreeBytes} bytes available to the current user on the "
            + "target. These totals are logical file sizes only: reparse points contribute nothing to "
            + "them, and they say nothing about how much space a copy would really need.",
            "The migration may not fit in the space that is currently available. This is not a "
            + "certainty: an overwrite or skip policy, objects that already exist at the target, how "
            + "reparse points are copied, and how the file system allocates sparse or compressed data "
            + "can all change the real requirement.",
            "Check the target's available space, or reduce what is being migrated, before starting the "
            + "copy. CopyPreflight does not write anything to the target, so it cannot confirm the "
            + "outcome.");
}

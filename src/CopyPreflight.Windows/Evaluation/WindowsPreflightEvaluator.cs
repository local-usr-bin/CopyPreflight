using System.Runtime.Versioning;
using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Turns what was observed about a source tree and a target directory into user-facing findings.
/// </summary>
/// <remarks>
/// <para>
/// This is the rule layer: the only place where source facts and target capabilities are combined.
/// It answers one question, <i>"given what was observed on both sides, what should the user know
/// before migrating?"</i>, and it produces nothing else.
/// </para>
/// <para>
/// Evaluation is a <b>pure computation</b>. It performs no file system I/O, does not re-scan the
/// source, does not re-probe the target, does not touch the target directory (not even to check
/// whether a destination path already exists) and never modifies anything. Given the same inputs it
/// always produces the same findings, in the same order, and it returns findings rather than printing
/// or storing them. What the target actually holds is passed in as a fact by whoever observed it.
/// </para>
/// <para>
/// The rules described here are Windows migration compatibility rules — <c>MAX_PATH</c> behaviour,
/// a FAT32 file-size ceiling, path component limits, Windows file and directory naming rules,
/// reparse point handling, and the objects a destination already holds — so this type lives in the
/// Windows layer next to <see cref="TargetProbeResult"/>, which it consumes.
/// </para>
/// <para>
/// The order of the returned sequence is <b>not</b> a product contract; it happens to be stable
/// because the rules always run in the same order. Presentation is free to sort by severity, code
/// or path. Tests must not depend on which position a particular finding occupies.
/// </para>
/// <para>
/// The type holds no state, so one instance can be reused for any number of evaluations.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsPreflightEvaluator
{
    /// <summary>
    /// Evaluates every implemented rule against one scanned source and one probed target, without any
    /// check of the objects the destinations already hold.
    /// </summary>
    /// <param name="source">What the scanner observed in the source tree.</param>
    /// <param name="target">What the target probe established about the target directory.</param>
    /// <returns>
    /// The findings the implemented rules produced, possibly empty. An empty result means
    /// "no known blocking issue was detected", never "the copy will succeed".
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Omitting the existing-object check does <b>not</b> mean that no object occupies the
    /// destinations. It is reported as what it is: an explicit coverage gap saying that nothing about
    /// destination collisions was examined. A caller that has carried out the check passes its result
    /// to <see cref="Evaluate(SourceScanResult, TargetProbeResult, TargetExistingObjectProbeResult?)"/>
    /// instead; a silence here would be indistinguishable from a clean result.
    /// </remarks>
    public IReadOnlyList<Finding> Evaluate(SourceScanResult source, TargetProbeResult target)
        => Evaluate(source, target, existingObjects: null);

    /// <summary>
    /// Evaluates every implemented rule against one scanned source, one probed target, and what a
    /// read-only check found at the mapped destinations.
    /// </summary>
    /// <param name="source">What the scanner observed in the source tree.</param>
    /// <param name="target">What the target probe established about the target directory.</param>
    /// <param name="existingObjects">
    /// What <see cref="WindowsTargetExistingObjectProbe"/> established about the objects already
    /// occupying the mapped destinations, or <see langword="null"/> when no check was carried out. A
    /// <see langword="null"/> value is reported as a coverage gap, never as "nothing was found".
    /// </param>
    /// <returns>
    /// The findings the implemented rules produced, possibly empty. An empty result means
    /// "no known blocking issue was detected", never "the copy will succeed".
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="target"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Source-only rules — the scan failures, the reparse point warning and the Windows naming rules
    /// — always run, because they describe facts about the source that remain true whatever the
    /// target turns out to be. The naming rules belong here rather than with the target-dependent
    /// rules because they consume no target capability and compose no destination path: whether a
    /// name contains an illegal character, stands for a reserved device or ends with a space is a
    /// property of the observed name, judged against ordinary Windows naming semantics. Losing those
    /// findings because the target happened to be unusable would hide known problems in the source.
    /// </para>
    /// <para>
    /// If the target is itself rejected as a destination (Windows refused the path, or no usable
    /// directory could be reached through it), the target-wide blocking finding is produced and the
    /// remaining target-dependent rules are <b>skipped</b>: composing destination paths under a
    /// target that cannot be used, or asking what a target's file system implies, would describe a
    /// migration that cannot happen anyway. The source findings already produced are kept, and the
    /// blocking finding already states that no target-dependent check could be carried out, so the
    /// existing-object coverage is not restated on top of it.
    /// </para>
    /// <para>
    /// When the target <i>is</i> usable, each capability that stayed unknown produces at most one
    /// target-wide informational finding, so the user learns which checks could not be carried out
    /// without being shown one finding per source entry. The existing-object facts follow the same
    /// principle: what could be established produces a finding per affected object, and what could
    /// not is summarised once rather than repeated for every object behind it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Finding> Evaluate(
        SourceScanResult source,
        TargetProbeResult target,
        TargetExistingObjectProbeResult? existingObjects)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var findings = new List<Finding>();

        // Source-only rules. These describe the source itself, so they apply regardless of whether
        // the target can be used.
        findings.AddRange(SourceScanIssueRule.Evaluate(source));
        findings.AddRange(ReparsePointRule.Evaluate(source));
        findings.AddRange(WindowsNameCompatibilityRule.Evaluate(source));
        findings.AddRange(SourceCaseCollisionRule.Evaluate(source));

        if (TargetPathBlockerRule.IsTargetBlocked(target))
        {
            findings.AddRange(TargetPathBlockerRule.Evaluate(target));

            return findings;
        }

        // Target-dependent rules, only meaningful for a target that can be used as a directory.
        findings.AddRange(TargetCapabilityCoverageRule.Evaluate(target.Capabilities));
        findings.AddRange(TargetExistingObjectRule.Evaluate(source, existingObjects));
        findings.AddRange(Fat32MaxFileSizeRule.Evaluate(source, target.Capabilities));
        findings.AddRange(TargetPathComponentRule.Evaluate(source, target.Capabilities));
        findings.AddRange(TraditionalPathLengthRule.Evaluate(source, target.TargetDirectoryPath));
        findings.AddRange(TargetFreeSpaceRule.Evaluate(source, target.Capabilities));

        return findings;
    }
}

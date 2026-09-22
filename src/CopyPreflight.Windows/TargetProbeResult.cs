using CopyPreflight.Core;

namespace CopyPreflight.Windows;

/// <summary>
/// Everything one probe of a target directory established.
/// </summary>
/// <remarks>
/// <para>
/// The result is a plain, inspectable value produced by
/// <see cref="WindowsTargetCapabilityProbe"/>. It reports what Windows was actually able to say
/// about the target — three independently known-or-unknown capabilities, plus the failures that
/// kept any of them unknown.
/// </para>
/// <para>
/// It is not a verdict. The result contains no <see cref="Finding"/> and no severity: a target whose
/// file system could not be described is <b>not</b> reported as a problem here, only as a capability
/// that stays unknown. Turning that into an Error, a Warning or an Info is the rule layer's job.
/// </para>
/// <para>
/// It also says nothing about writability. CopyPreflight never writes a probe file, creates a
/// directory or changes an ACL, so "the path is currently reachable and Windows answered these
/// queries" is the strongest claim this result supports.
/// </para>
/// </remarks>
public sealed class TargetProbeResult
{
    /// <summary>Creates a probe result.</summary>
    /// <param name="targetDirectoryPath">
    /// The normalized absolute target directory path the probe was applied to.
    /// </param>
    /// <param name="capabilities">The capabilities that could be established, and those that could not.</param>
    /// <param name="issues">The failures recorded while probing. Empty when every query was answered.</param>
    /// <exception cref="ArgumentException"><paramref name="targetDirectoryPath"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="capabilities"/> or <paramref name="issues"/> is <see langword="null"/>.
    /// </exception>
    public TargetProbeResult(
        string targetDirectoryPath,
        TargetCapabilities capabilities,
        IReadOnlyList<TargetProbeIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(issues);

        TargetDirectoryPath = string.IsNullOrWhiteSpace(targetDirectoryPath)
            ? throw new ArgumentException(
                "A target directory path must not be null, empty or whitespace.",
                nameof(targetDirectoryPath))
            : targetDirectoryPath;

        Capabilities = capabilities;
        Issues = issues;
    }

    /// <summary>
    /// The normalized absolute path of the target directory. Every path reported by this probe is
    /// derived from it, and the target directory itself is the destination root of a migration.
    /// </summary>
    public string TargetDirectoryPath { get; }

    /// <summary>
    /// What the target's file system is known to do, with everything that could not be observed kept
    /// explicitly unknown. Capabilities are established independently: one failing query never
    /// discards another one that succeeded.
    /// </summary>
    public TargetCapabilities Capabilities { get; }

    /// <summary>
    /// The failures recorded while probing, empty when every query was answered. Each capability
    /// whose query failed is unknown, and the matching issue explains why.
    /// </summary>
    public IReadOnlyList<TargetProbeIssue> Issues { get; }
}

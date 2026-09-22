using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Builds the inputs the existing-object check and its rule consume, and runs the check over a scripted
/// file-system seam.
/// </summary>
/// <remarks>
/// The check itself is exercised against the real file system by
/// <see cref="TargetExistingObjectProbeIntegrationTests"/>. The scripted seam is used for the outcomes a
/// developer machine cannot honestly produce — an ancestor that answers differently from its child, a
/// share root that refuses the attribute query while its children answer, an access-denied parent — and
/// it is strict by default: asking about a path the test did not script is a test defect rather than a
/// silently defaulted answer.
/// </remarks>
internal static class ExistingObjectInputs
{
    /// <summary>A target root short enough not to disturb a path length on its own.</summary>
    internal const string TargetRoot = @"C:\target";

    internal static SourceScanResult Source(params SourceEntry[] entries)
        => new(@"C:\source", entries, []);

    /// <summary>A target that the capability probe found usable.</summary>
    internal static TargetProbeResult UsableTarget(string targetDirectoryPath = TargetRoot)
        => PreflightInputs.HealthyTarget(targetDirectoryPath);

    /// <summary>A target the capability probe rejected as a destination.</summary>
    internal static TargetProbeResult RejectedTarget(string targetDirectoryPath = TargetRoot)
        => PreflightInputs.BlockedTarget(TargetProbeIssueKind.TargetPathUnavailable, targetDirectoryPath);

    /// <summary>
    /// A completed result over the given observations, with the outcome derived exactly as the check
    /// derives it.
    /// </summary>
    internal static TargetExistingObjectProbeResult ProbeResult(params ExistingObjectObservation[] observations)
        => ProbeResult(
            TargetRootObservation.Answered(TargetRootObservationKind.Directory, "The test target root."),
            observations);

    internal static TargetExistingObjectProbeResult ProbeResult(
        TargetRootObservation targetRoot,
        params ExistingObjectObservation[] observations)
        => new(
            targetRoot,
            observations,
            TargetExistingObjectProbeResult.HasFullCoverage(targetRoot, observations)
                ? TargetExistingObjectProbeOutcome.Completed
                : TargetExistingObjectProbeOutcome.CompletedWithCoverageGaps);

    /// <summary>A result of a run that was interrupted, as a caller that caught the cancellation reports it.</summary>
    internal static TargetExistingObjectProbeResult CancelledProbeResult(
        params ExistingObjectObservation[] observations)
        => new(
            TargetRootObservation.Answered(TargetRootObservationKind.Directory, "The test target root."),
            observations,
            TargetExistingObjectProbeOutcome.Cancelled);

    /// <summary>The mapped location a source relative path composes to under a target root.</summary>
    internal static string Mapped(string relativePath, string targetRoot = TargetRoot)
        => Path.Join(targetRoot, relativePath);

    /// <summary>Runs the check over a scripted seam, with unscripted paths rejected.</summary>
    internal static (TargetExistingObjectProbeResult Result, ScriptedFileSystemApi Api) Run(
        SourceScanResult source,
        Action<ScriptedFileSystemApi> script,
        string targetRoot = TargetRoot)
    {
        var api = new ScriptedFileSystemApi().RequiringScriptedAttributePaths();
        script(api);

        var result = new WindowsTargetExistingObjectProbe(api).Probe(source, UsableTarget(targetRoot));

        return (result, api);
    }

    /// <summary>Runs the check over the target the capability probe rejected.</summary>
    internal static (TargetExistingObjectProbeResult Result, ScriptedFileSystemApi Api) RunAgainstRejectedTarget(
        SourceScanResult source,
        string targetRoot = TargetRoot)
    {
        var api = new ScriptedFileSystemApi().RequiringScriptedAttributePaths();
        var result = new WindowsTargetExistingObjectProbe(api).Probe(source, RejectedTarget(targetRoot));

        return (result, api);
    }

    /// <summary>
    /// A canonical, order-independent representation of the observations, for proving that the
    /// inventory's arrival order does not change what was concluded.
    /// </summary>
    internal static IReadOnlyList<string> ObservationsShape(this TargetExistingObjectProbeResult result)
        => [.. result.Observations
            .Select(observation => string.Join(
                "|",
                observation.SourceRelativePath,
                observation.Kind,
                observation.Presence?.ToString() ?? "<none>",
                observation.GapReason?.ToString() ?? "<none>",
                observation.BlockingAncestorRelativePath ?? "<none>",
                observation.NativeErrorCode?.ToString() ?? "<none>"))
            .OrderBy(text => text, StringComparer.Ordinal)];
}

/// <summary>Queries over observations and findings that never assume a position in a list.</summary>
internal static class ExistingObjectQueries
{
    /// <summary>The one observation belonging to a source relative path.</summary>
    internal static ExistingObjectObservation ObservationFor(
        this TargetExistingObjectProbeResult result,
        string sourceRelativePath)
    {
        var matches = result.Observations
            .Where(observation => observation.SourceRelativePath == sourceRelativePath)
            .ToList();

        Assert.Single(matches);

        return matches[0];
    }

    /// <summary>Whether any observation is of the given kind.</summary>
    internal static bool HasKind(
        this TargetExistingObjectProbeResult result,
        ExistingObjectObservationKind kind)
        => result.Observations.Any(observation => observation.Kind == kind);

    internal static IReadOnlyList<Finding> EvaluateWith(
        this SourceScanResult source,
        TargetProbeResult target,
        TargetExistingObjectProbeResult? existingObjects)
        => new WindowsPreflightEvaluator().Evaluate(source, target, existingObjects);

    /// <summary>
    /// Asserts the exact sequence of paths the check asked the attribute query about, in the order it
    /// asked. Which questions are asked — and which are deliberately not — is part of the check's
    /// contract, not an implementation detail.
    /// </summary>
    internal static void AssertAttributeQueries(
        this ScriptedFileSystemApi api,
        params string[] expectedPaths)
        => Assert.Equal(expectedPaths, api.AttributePaths);
}

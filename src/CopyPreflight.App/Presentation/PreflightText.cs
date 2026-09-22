using System.Globalization;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.App;

/// <summary>
/// The states the check screen can be in.
/// </summary>
/// <remarks>
/// <para>
/// This is the presentation's own state, and it is deliberately not the same thing as
/// <see cref="CopyPreflight.Windows.PreflightRunStatus"/>. A run has ended in exactly one of three
/// ways; a screen can additionally be waiting for input, running, or waiting for a cancellation to be
/// honoured. Keeping the two apart is what stops "the run finished" and "the screen was never used"
/// from ever being rendered as the same thing.
/// </para>
/// <para>
/// The mapping between them is fixed and one-way: a finished run's status becomes
/// <see cref="Completed"/>, <see cref="Cancelled"/> or <see cref="Failed"/>, and nothing else.
/// </para>
/// </remarks>
public enum CheckState
{
    /// <summary>Nothing has been checked yet, or the input changed and the previous result was dropped.</summary>
    Idle = 0,

    /// <summary>A check is running.</summary>
    Running = 1,

    /// <summary>
    /// Cancellation has been requested and the run has not stopped yet. The screen stays busy, because
    /// the work is genuinely still going on.
    /// </summary>
    Cancelling = 2,

    /// <summary>A check ran to the end. Its findings and its coverage gaps are both available.</summary>
    Completed = 3,

    /// <summary>A check was cancelled before it finished, so there is no assessment.</summary>
    Cancelled = 4,

    /// <summary>A check stopped on an unexpected failure, so there is no assessment.</summary>
    Failed = 5,
}

/// <summary>
/// The wording the check screen uses, kept in one place so it can be asserted without a window.
/// </summary>
/// <remarks>
/// <para>
/// Two rules run through all of this text. The first is that the screen never states more than was
/// established: "Unknown" is a value that is shown, not a blank, and a stage that never ran says so
/// instead of showing a zero. The second is that no wording here may promise anything about a copy —
/// the strongest conclusion available is the frozen
/// <see cref="NoKnownBlockingIssue"/> sentence, and it is only reachable from a check that finished
/// without a blocking finding.
/// </para>
/// <para>
/// Numbers are formatted with the invariant culture: the interface is English, and a report that
/// reformatted its own counts from machine to machine would be needlessly hard to compare.
/// </para>
/// </remarks>
internal static class PreflightText
{
    /// <summary>
    /// The frozen conclusion from <c>docs/PRODUCT_SPEC.md</c> §6, used verbatim. It is the strongest
    /// thing that may be said about a check that finished with no blocking finding.
    /// </summary>
    internal const string NoKnownBlockingIssue = "No known blocking issue was detected.";

    /// <summary>Shown when a check finished but part of its scope was never observed.</summary>
    internal const string CoverageGaps = "Assessment ended with coverage gaps.";

    /// <summary>Shown for a run that was cancelled and therefore assessed nothing.</summary>
    internal const string NotAnAssessment = "No assessment was produced.";

    /// <summary>How a finding that belongs to no single source object is labelled.</summary>
    internal const string TargetWide = "Target-wide (not tied to a single source object)";

    /// <summary>The label for a fact that was never observed because the stage never ran.</summary>
    internal const string NotChecked = "Not checked";

    /// <summary>The label for a fact that was asked about and could not be established.</summary>
    internal const string Unknown = "Unknown";

    /// <summary>The severity of a finding as it is displayed.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="severity"/> is not a known severity.</exception>
    internal static string DescribeSeverity(Severity severity) => severity switch
    {
        Severity.Error => "Error",
        Severity.Warning => "Warning",
        Severity.Info => "Info",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown severity."),
    };

    /// <summary>
    /// The display order of the severities: blocking first, informational last.
    /// </summary>
    /// <remarks>
    /// Written out rather than taken from the enum's numeric order. The numbers happen to agree today,
    /// but the ordering a user sees is a presentation decision, and it must not silently follow a
    /// declaration that a later milestone is free to reorder.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="severity"/> is not a known severity.</exception>
    internal static int SeverityRank(Severity severity) => severity switch
    {
        Severity.Error => 0,
        Severity.Warning => 1,
        Severity.Info => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown severity."),
    };

    /// <summary>The stage a check is in, as it is displayed.</summary>
    internal static string DescribePhase(PreflightRunPhase phase) => phase switch
    {
        PreflightRunPhase.ScanningSource => "Scanning the source directory",
        PreflightRunPhase.ProbingTargetCapabilities => "Probing what Windows reports about the target",
        PreflightRunPhase.ProbingExistingObjects => "Checking what already occupies the destinations",
        PreflightRunPhase.EvaluatingRules => "Deriving the findings",
        _ => "Working",
    };

    /// <summary>What the check screen is doing, as it is displayed.</summary>
    internal static string DescribeState(CheckState state) => state switch
    {
        CheckState.Idle => "Ready. Nothing has been checked yet.",
        CheckState.Running => "Checking…",
        CheckState.Cancelling => "Cancellation requested. Waiting for the current stage to stop.",
        CheckState.Completed => "Assessment finished.",
        CheckState.Cancelled => "Assessment cancelled before it finished.",
        CheckState.Failed => "The check failed to run to completion.",
        _ => "Ready. Nothing has been checked yet.",
    };

    /// <summary>
    /// What one stage of a check covered, as it is displayed.
    /// </summary>
    /// <param name="phase">The stage the label belongs to.</param>
    /// <param name="coverage">What that stage covered.</param>
    /// <param name="detail">
    /// Why the stage is not complete, or <see langword="null"/> when there is nothing to add.
    /// </param>
    internal static string DescribeCoverage(
        string phase,
        PreflightPhaseCoverage coverage,
        string? detail = null)
    {
        var state = coverage switch
        {
            PreflightPhaseCoverage.Complete => "complete",
            PreflightPhaseCoverage.Incomplete => "incomplete",
            _ => "not performed",
        };

        return string.IsNullOrWhiteSpace(detail) ? $"{phase}: {state}" : $"{phase}: {state} — {detail}";
    }

    /// <summary>
    /// One observed count.
    /// </summary>
    /// <param name="label">What is being counted.</param>
    /// <param name="count">The count.</param>
    /// <param name="isPartial">
    /// Whether the count is a partial observation rather than the total of a finished observation. A
    /// partial count says so, because a count of what was seen is not a count of what is there.
    /// </param>
    internal static string DescribeCount(string label, long count, bool isPartial)
        => isPartial
            ? $"{label}: {FormatCount(count)} observed so far"
            : $"{label}: {FormatCount(count)}";

    /// <summary>
    /// A count that was never produced, because the stage that would have observed it never ran.
    /// </summary>
    /// <remarks>
    /// It is deliberately not rendered as <c>0</c>. A zero is a real observation — a source tree that
    /// genuinely holds no files yields one — and showing it for a source tree that was never looked at
    /// would state the opposite of what happened.
    /// </remarks>
    internal static string DescribeNotObserved(string label) => $"{label}: not observed";

    /// <summary>
    /// The target's file system name: the observed name, the explicit statement that it is unknown, or
    /// the fact that it was never asked about.
    /// </summary>
    /// <remarks>
    /// An unknown capability is never rendered as a zero, a blank or a default. The diagnostic recorded
    /// when it stayed unknown is shown with it, because "the amount is unknown" and "the query did not
    /// answer" are different things to the person deciding what to do next.
    /// </remarks>
    internal static string DescribeFileSystemName(TargetProbeResult? target)
    {
        if (target is null)
        {
            return $"{FileSystemNameLabel}: {NotChecked}";
        }

        return target.Capabilities.FileSystemName.TryGetValue(out var name)
            ? $"{FileSystemNameLabel}: {name.Value}"
            : DescribeUnknown(FileSystemNameLabel, target.Capabilities.FileSystemName.Diagnostic);
    }

    /// <summary>
    /// The space Windows reported as available to the current user, or the explicit statement that it
    /// could not be reported.
    /// </summary>
    internal static string DescribeFreeSpace(TargetProbeResult? target)
    {
        if (target is null)
        {
            return $"{FreeSpaceLabel}: {NotChecked}";
        }

        return target.Capabilities.AvailableFreeBytes.TryGetValue(out var bytes)
            ? $"{FreeSpaceLabel}: {FormatCount(bytes)} bytes"
            : DescribeUnknown(FreeSpaceLabel, target.Capabilities.AvailableFreeBytes.Diagnostic);
    }

    /// <summary>
    /// The longest single name the target accepts, or the explicit statement that it could not be
    /// established.
    /// </summary>
    internal static string DescribeComponentLimit(TargetProbeResult? target)
    {
        if (target is null)
        {
            return $"{ComponentLimitLabel}: {NotChecked}";
        }

        return target.Capabilities.MaxPathComponentLength.TryGetValue(out var maximum)
            ? $"{ComponentLimitLabel}: {FormatCount(maximum)} characters"
            : DescribeUnknown(ComponentLimitLabel, target.Capabilities.MaxPathComponentLength.Diagnostic);
    }

    /// <summary>Why a pair of entered paths was refused, as it is displayed.</summary>
    internal static string DescribeRequestError(PreflightRequestError error) => error switch
    {
        PreflightRequestError.SourcePathMissing =>
            "Enter the source folder to check.",
        PreflightRequestError.TargetPathMissing =>
            "Enter the target folder to check.",
        PreflightRequestError.SourcePathNotFullyQualified =>
            "The source folder must be a complete path, such as C:\\Data or \\\\server\\share\\data. "
            + "A relative path is not resolved against the folder the application was started from, "
            + "because that would check something other than what you typed.",
        PreflightRequestError.TargetPathNotFullyQualified =>
            "The target folder must be a complete path, such as C:\\Data or \\\\server\\share\\data. "
            + "A relative path is not resolved against the folder the application was started from, "
            + "because that would check something other than what you typed.",
        _ => "Enter a source folder and a target folder.",
    };

    /// <summary>
    /// How the path that was asked about relates to the path that was actually used.
    /// </summary>
    /// <remarks>
    /// The scanner and the probes resolve the paths they are given, and both report the resolved root
    /// back on their results. Showing the resolved path whenever it differs from what was typed is what
    /// keeps the check from quietly examining a different location than the one on screen.
    /// </remarks>
    internal static string DescribePathAgreement(string label, string entered, string? resolved)
        => string.IsNullOrEmpty(resolved) || string.Equals(entered, resolved, StringComparison.Ordinal)
            ? $"{label}: {entered}"
            : $"{label}: {resolved}   (entered as: {entered})";

    /// <summary>Why a run failed, as it is displayed.</summary>
    internal static string DescribeFailure(PreflightRunFailure failure)
        => $"{failure.ExceptionTypeName} while {DescribePhase(failure.Phase).ToLowerInvariant()}: "
        + failure.Message;

    /// <summary>A count, grouped and formatted the same way on every machine.</summary>
    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A capability that was asked about and could not be established, with the reason recorded.</summary>
    private static string DescribeUnknown(string label, string? diagnostic)
        => $"{label}: {Unknown} — "
        + (string.IsNullOrWhiteSpace(diagnostic) ? "no reason was recorded" : diagnostic);

    /// <summary>Labels for the three capabilities the target probe establishes.</summary>
    private const string FileSystemNameLabel = "File system";

    private const string FreeSpaceLabel = "Space available to the current user";

    private const string ComponentLimitLabel = "Maximum name component length";
}

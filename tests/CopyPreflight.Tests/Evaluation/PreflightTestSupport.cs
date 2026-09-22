using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Builds the two inputs the evaluator consumes, straight from the existing domain and result
/// models.
/// </summary>
/// <remarks>
/// Rule evaluation is a pure computation, so these tests need no file system at all: they construct
/// the facts they want to reason about instead of arranging real directories. That also keeps the
/// suite honest — a rule must not depend on anything the constructed facts do not state.
/// </remarks>
internal static class PreflightInputs
{
    /// <summary>A target root short enough that it never disturbs a path length calculation on its own.</summary>
    internal const string TargetRoot = @"C:\target";

    internal static SourceScanResult Source(params SourceEntry[] entries)
        => new(@"C:\source", entries, []);

    internal static SourceScanResult SourceWithIssues(
        IReadOnlyList<SourceScanIssue> issues,
        params SourceEntry[] entries)
        => new(@"C:\source", entries, issues);

    internal static SourceScanIssue Issue(
        SourceScanIssueKind kind,
        string? relativePath,
        string? exceptionTypeName = "System.IO.IOException",
        int? hResult = -2147024891)
        => new(kind, relativePath, exceptionTypeName, hResult, "A deterministic diagnostic.");

    /// <summary>A target that is usable as a directory, with exactly the capabilities supplied.</summary>
    internal static TargetProbeResult Target(
        CapabilityValue<FileSystemName> fileSystemName,
        CapabilityValue<long> availableFreeBytes,
        CapabilityValue<int> maxPathComponentLength,
        string targetDirectoryPath = TargetRoot,
        params TargetProbeIssue[] issues)
        => new(
            targetDirectoryPath,
            new TargetCapabilities(fileSystemName, availableFreeBytes, maxPathComponentLength),
            issues);

    /// <summary>An NTFS target with ample space and room for long names: the neutral baseline.</summary>
    internal static TargetProbeResult HealthyTarget(string targetDirectoryPath = TargetRoot)
        => Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("NTFS")),
            CapabilityValue<long>.Known(long.MaxValue),
            CapabilityValue<int>.Known(255),
            targetDirectoryPath);

    internal static TargetProbeResult BlockedTarget(
        TargetProbeIssueKind kind,
        string targetDirectoryPath = TargetRoot)
        => Target(
            CapabilityValue<FileSystemName>.Unknown("The target path is not an available directory."),
            CapabilityValue<long>.Unknown("The target path is not an available directory."),
            CapabilityValue<int>.Unknown("The target path is not an available directory."),
            targetDirectoryPath,
            new TargetProbeIssue(
                kind,
                targetDirectoryPath,
                kind == TargetProbeIssueKind.TargetPathInvalid ? "GetFileAttributesW" : "GetDiskFreeSpaceExW",
                kind == TargetProbeIssueKind.TargetPathInvalid ? 123 : 3,
                "The target could not be used."));

    internal static IReadOnlyList<Finding> Evaluate(SourceScanResult source, TargetProbeResult target)
        => new WindowsPreflightEvaluator().Evaluate(source, target);

    /// <summary>
    /// Evaluates with the facts a read-only check established about the mapped destinations.
    /// </summary>
    /// <param name="existingObjects">
    /// Those facts, or <see langword="null"/> to model an evaluation that was given none.
    /// </param>
    internal static IReadOnlyList<Finding> Evaluate(
        SourceScanResult source,
        TargetProbeResult target,
        TargetExistingObjectProbeResult? existingObjects)
        => new WindowsPreflightEvaluator().Evaluate(source, target, existingObjects);

    /// <summary>
    /// The facts of a completed existing-object check that found nothing at the mapped destination of
    /// every source entry.
    /// </summary>
    /// <remarks>
    /// Tests whose subject is a source-only or capability rule use this so the absence of collision
    /// findings follows from stated facts rather than from a check that was never supplied. It keeps
    /// the destination check out of their picture without them having to depend on the
    /// "check not performed" gap finding.
    /// </remarks>
    internal static TargetExistingObjectProbeResult FoundNothingAtDestinations(SourceScanResult source)
        => FoundNothingAt([.. source.Entries.Select(entry => entry.RelativePath)]);

    /// <summary>The facts of a completed check that found nothing at each of the given destinations.</summary>
    internal static TargetExistingObjectProbeResult FoundNothingAt(params string[] relativePaths)
        => ExistingObjectInputs.ProbeResult(
            [.. relativePaths.Select(relativePath => ExistingObjectObservation.ObservedMissing(
                relativePath,
                ExistingObjectInputs.Mapped(relativePath),
                ScriptedFileSystemApi.ErrorFileNotFound,
                "The test states that nothing occupies this destination."))]);
}

/// <summary>Queries over a finding list that never assume a particular position in it.</summary>
internal static class FindingQueries
{
    /// <summary>
    /// The findings carrying one code. Finding order is deliberately not part of the rule layer's
    /// contract, so every assertion goes through a code rather than an index.
    /// </summary>
    internal static IReadOnlyList<Finding> WithCode(this IReadOnlyList<Finding> findings, FindingCode code)
        => [.. findings.Where(finding => finding.Code == code)];

    internal static Finding Only(this IReadOnlyList<Finding> findings, FindingCode code)
    {
        var matches = findings.WithCode(code);

        Assert.Single(matches);

        return matches[0];
    }

    /// <summary>A stable, order-independent representation of a finding set, for comparisons.</summary>
    internal static IReadOnlyList<string> Shape(this IReadOnlyList<Finding> findings)
        => [.. findings
            .Select(finding => $"{finding.Severity}|{finding.Code.Value}|{finding.SourceRelativePath ?? "<none>"}")
            .OrderBy(text => text, StringComparer.Ordinal)];
}

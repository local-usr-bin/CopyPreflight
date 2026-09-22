using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Cross-cutting properties of the evaluation entry point: purity, determinism and the finding
/// text invariants.
/// </summary>
public class WindowsPreflightEvaluatorTests
{
    [Fact]
    public void BothInputsAreRequired()
    {
        var evaluator = new WindowsPreflightEvaluator();

        Assert.Throws<ArgumentNullException>(
            () => evaluator.Evaluate(source: null!, PreflightInputs.HealthyTarget()));
        Assert.Throws<ArgumentNullException>(
            () => evaluator.Evaluate(PreflightInputs.Source(), target: null!));
    }

    [Fact]
    public void TheSameInputsAlwaysProduceTheSameFindings()
    {
        var source = RichSource();
        var target = RichTarget();
        var evaluator = new WindowsPreflightEvaluator();

        var first = evaluator.Evaluate(source, target);
        var second = evaluator.Evaluate(source, target);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheOrderOfTheInventoryIsNotPartOfTheResult()
    {
        // Enumeration order is a file system artifact, so reversing it must not change what is
        // reported — only, at most, the order it is reported in.
        var source = RichSource();

        // Only the inventory order changes; the observed facts, including the scan issues, stay the same.
        var reversed = PreflightInputs.SourceWithIssues(source.Issues, source.Entries.Reverse().ToArray());

        var forward = PreflightInputs.Evaluate(source, RichTarget()).Shape();
        var backward = PreflightInputs.Evaluate(reversed, RichTarget()).Shape();

        Assert.NotEmpty(forward);
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void EvaluationReadsNothingFromTheFileSystem()
    {
        // The paths below deliberately point at nothing. If evaluation inspected the target or the
        // source it would have to notice; because it only reasons about the facts it was handed, the
        // target-dependent rules still report exactly what those facts imply.
        var missingTargetPath = Path.Combine(
            Path.GetTempPath(),
            "copy-preflight-missing-" + Guid.NewGuid().ToString("N"));

        Assert.False(Directory.Exists(missingTargetPath));
        Assert.False(File.Exists(missingTargetPath));

        var relativePath = new string('p', 300);
        var source = PreflightInputs.Source(SourceEntry.File(relativePath, 8L * 1024 * 1024 * 1024));

        var target = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("FAT32")),
            CapabilityValue<long>.Known(1),
            CapabilityValue<int>.Known(8),
            missingTargetPath);

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetComponentTooLong));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TargetSpaceMayBeInsufficient));
    }

    [Fact]
    public void EveryFindingCarriesFourNonEmptyTexts()
    {
        var findings = PreflightInputs.Evaluate(RichSource(), RichTarget());

        Assert.NotEmpty(findings);

        foreach (var finding in findings)
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.Title));
            Assert.False(string.IsNullOrWhiteSpace(finding.Explanation));
            Assert.False(string.IsNullOrWhiteSpace(finding.Impact));
            Assert.False(string.IsNullOrWhiteSpace(finding.Recommendation));

            // A title stays short; the explanation is where the reasoning goes.
            Assert.True(finding.Title.Length <= 80, $"title too long: {finding.Title}");
        }
    }

    [Fact]
    public void NothingIsReportedWhenNothingWasObserved()
    {
        // No findings means "no known blocking issue was detected". It is never reported as a
        // reassuring success finding, because CopyPreflight cannot promise that a copy will succeed.
        var findings = PreflightInputs.Evaluate(PreflightInputs.Source(), PreflightInputs.HealthyTarget());

        Assert.Empty(findings);
    }

    [Fact]
    public void OnlyProblemsAreReported()
    {
        // A clean, well-described target and a healthy source produce nothing at all: there is no
        // "all clear" finding, and no information is invented to fill the report.
        var source = PreflightInputs.Source(
            SourceEntry.File(@"docs\report.docx", 1024),
            SourceEntry.Directory("media"),
            SourceEntry.File(@"media\clip.mp4", 2048));

        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget());

        Assert.Empty(findings);
    }

    /// <summary>
    /// A source whose facts trigger several rules at once, with the entries in a fixed order so the
    /// order-independence test has something stable to reverse.
    /// </summary>
    private static SourceScanResult RichSource()
    {
        return PreflightInputs.SourceWithIssues(
            [
                PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked"),
                PreflightInputs.Issue(SourceScanIssueKind.CannotReadEntryMetadata, "unknown.bin"),
            ],
            SourceEntry.File(new string('n', 300), 8L * 1024 * 1024 * 1024),
            SourceEntry.ReparsePoint(@"links\link", SourceEntryKind.File),
            SourceEntry.Directory("media"),
            SourceEntry.File(@"media\clip.mp4", 2048));
    }

    private static TargetProbeResult RichTarget()
        => PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName("FAT32")),
            CapabilityValue<long>.Known(1024),
            CapabilityValue<int>.Known(8));
}

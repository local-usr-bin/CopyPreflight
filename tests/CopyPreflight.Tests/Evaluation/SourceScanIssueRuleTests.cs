using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// Mapping from the scanner's observation failures to user-visible findings.
/// </summary>
public class SourceScanIssueRuleTests
{
    [Theory]
    [InlineData(SourceScanIssueKind.CannotEnumerateDirectory)]
    [InlineData(SourceScanIssueKind.CannotReadEntryMetadata)]
    public void EveryScanFailureBecomesAnError(SourceScanIssueKind kind)
    {
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(kind, @"photos\holiday")]);

        var finding = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .Only(ExpectedCode(kind));

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(@"photos\holiday", finding.SourceRelativePath);
    }

    [Fact]
    public void TheTwoScanFailuresGetDistinctCodes()
    {
        var source = PreflightInputs.SourceWithIssues(
            [
                PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, "locked"),
                PreflightInputs.Issue(SourceScanIssueKind.CannotReadEntryMetadata, @"locked\unknown.bin"),
            ]);

        var findings = PreflightInputs.Evaluate(
            source,
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(source));

        Assert.Equal(2, findings.Count);
        Assert.Equal(
            WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed,
            findings.Single(finding => finding.SourceRelativePath == "locked").Code);
        Assert.Equal(
            WindowsPreflightFindingCodes.SourceEntryMetadataFailed,
            findings.Single(finding => finding.SourceRelativePath == @"locked\unknown.bin").Code);
    }

    [Fact]
    public void AFailureThatConcernsTheSourceRootItselfCarriesNoSourcePath()
    {
        // The source root is never an entry, so an issue about it has no relative path to show.
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotEnumerateDirectory, relativePath: null)]);

        var finding = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed);

        Assert.Null(finding.SourceRelativePath);
    }

    [Fact]
    public void AScanFailureNeverClaimsThatACopyIsCertainToFail()
    {
        // The scanner only established that it could not look; it did not establish that the copy
        // would fail, and the finding must not upgrade one statement into the other.
        foreach (var kind in new[]
                 {
                     SourceScanIssueKind.CannotEnumerateDirectory,
                     SourceScanIssueKind.CannotReadEntryMetadata,
                 })
        {
            var finding = PreflightInputs.Evaluate(
                    PreflightInputs.SourceWithIssues([PreflightInputs.Issue(kind, "something")]),
                    PreflightInputs.HealthyTarget())
                .Only(ExpectedCode(kind));

            Assert.Contains("does not mean", finding.Impact, StringComparison.Ordinal);
            Assert.DoesNotContain("will fail", finding.Impact, StringComparison.Ordinal);
            Assert.DoesNotContain("cannot be copied", finding.Impact, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheObservedFailureIsDescribedWithoutTheLocalizedMessage()
    {
        var source = PreflightInputs.SourceWithIssues(
            [
                PreflightInputs.Issue(
                    SourceScanIssueKind.CannotReadEntryMetadata,
                    @"docs\report.docx",
                    exceptionTypeName: "System.UnauthorizedAccessException",
                    hResult: unchecked((int)0x80070005)),
            ]);

        var finding = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceEntryMetadataFailed);

        Assert.Contains("System.UnauthorizedAccessException", finding.Explanation, StringComparison.Ordinal);
        Assert.Contains("0x80070005", finding.Explanation, StringComparison.Ordinal);

        // The issue's Diagnostic is localized text; it must not leak into a rule's output.
        Assert.DoesNotContain("A deterministic diagnostic.", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIssueWithoutAnyFailureDetailIsStillDescribed()
    {
        var source = PreflightInputs.SourceWithIssues(
            [PreflightInputs.Issue(SourceScanIssueKind.CannotReadEntryMetadata, "orphan", null, null)]);

        var finding = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceEntryMetadataFailed);

        Assert.False(string.IsNullOrWhiteSpace(finding.Explanation));
    }

    [Fact]
    public void ACompleteScanProducesNoScanFailureFinding()
    {
        var findings = PreflightInputs.Evaluate(PreflightInputs.Source(), PreflightInputs.HealthyTarget());

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed));
        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.SourceEntryMetadataFailed));
    }

    private static FindingCode ExpectedCode(SourceScanIssueKind kind)
        => kind == SourceScanIssueKind.CannotEnumerateDirectory
            ? WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed
            : WindowsPreflightFindingCodes.SourceEntryMetadataFailed;
}

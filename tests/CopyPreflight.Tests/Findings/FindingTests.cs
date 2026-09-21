using CopyPreflight.Core;

namespace CopyPreflight.Tests;

/// <summary>
/// Structure and invariants of a rule-produced finding.
/// </summary>
public class FindingTests
{
    private const string SampleCode = "PATH_COMPONENT_TOO_LONG";
    private const string SampleTitle = "A path component is too long for the target";
    private const string SampleExplanation = "The component is 300 characters long and exceeds the target limit.";
    private const string SampleImpact = "The entry cannot be created at the target location.";
    private const string SampleRecommendation = "Shorten the name before copying.";

    [Fact]
    public void StoresEveryPartOfTheFinding()
    {
        var finding = Build(sourceRelativePath: @"photos\holiday\2024\a-very-long-name.jpg");

        Assert.Equal(new FindingCode(SampleCode), finding.Code);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(SampleTitle, finding.Title);
        Assert.Equal(SampleExplanation, finding.Explanation);
        Assert.Equal(SampleImpact, finding.Impact);
        Assert.Equal(SampleRecommendation, finding.Recommendation);
        Assert.Equal(@"photos\holiday\2024\a-very-long-name.jpg", finding.SourceRelativePath);
    }

    [Fact]
    public void AllowsAFindingThatBelongsToNoSingleSourceEntry()
    {
        // Target-wide and scan-wide findings legitimately carry no source path.
        Assert.Null(Build().SourceRelativePath);
    }

    [Fact]
    public void IsValueEqualWhenItsContentMatches()
    {
        Assert.Equal(Build(sourceRelativePath: "a.txt"), Build(sourceRelativePath: "a.txt"));
        Assert.NotEqual(Build(sourceRelativePath: "a.txt"), Build(sourceRelativePath: "b.txt"));
        Assert.NotEqual(Build(sourceRelativePath: "a.txt"), Build());
    }

    [Fact]
    public void RequiresACode()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new Finding(null!, Severity.Info, SampleTitle, SampleExplanation, SampleImpact, SampleRecommendation);
        });
    }

    [Fact]
    public void RejectsMissingTitle()
    {
        Assert.Throws<ArgumentException>(() => { _ = Build(title: null!); });
    }

    [Fact]
    public void RejectsBlankExplanation()
    {
        Assert.Throws<ArgumentException>(() => { _ = Build(explanation: "  "); });
    }

    [Fact]
    public void RejectsBlankImpact()
    {
        Assert.Throws<ArgumentException>(() => { _ = Build(impact: string.Empty); });
    }

    [Fact]
    public void RejectsBlankRecommendation()
    {
        Assert.Throws<ArgumentException>(() => { _ = Build(recommendation: "\t"); });
    }

    [Fact]
    public void RejectsABlankSourcePathWhenOneIsSupplied()
    {
        Assert.Throws<ArgumentException>(() => { _ = Build(sourceRelativePath: " "); });
    }

    private static Finding Build(
        FindingCode? code = null,
        Severity severity = Severity.Error,
        string? title = SampleTitle,
        string? explanation = SampleExplanation,
        string? impact = SampleImpact,
        string? recommendation = SampleRecommendation,
        string? sourceRelativePath = null)
        => new(
            code ?? new FindingCode(SampleCode),
            severity,
            title!,
            explanation!,
            impact!,
            recommendation!,
            sourceRelativePath);
}

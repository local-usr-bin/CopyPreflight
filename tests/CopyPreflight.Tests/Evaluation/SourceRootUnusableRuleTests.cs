using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The rule that turns a source root the scanner refused into a structured, source-side conclusion.
/// </summary>
/// <remarks>
/// <para>
/// Two things are being pinned here. The first is that an unusable root is reported at all, and as an
/// Error: an expected outcome of the input must reach the user as a finding rather than as a failure
/// of the application.
/// </para>
/// <para>
/// The second is the wording. The two admissible reasons are different facts and are never merged into
/// one sentence, and no text may name the internal precondition exception: its type name and namespace
/// describe a class, not a folder, and the user typed a path.
/// </para>
/// </remarks>
public class SourceRootUnusableRuleTests
{
    [Theory]
    [InlineData(InvalidSourceRootReason.NotFound)]
    [InlineData(InvalidSourceRootReason.NotADirectory)]
    public void AnUnusableRootBecomesExactlyOneSourceSideError(InvalidSourceRootReason reason)
    {
        var findings = PreflightInputs.Evaluate(
            PreflightInputs.UnusableRootSource(reason),
            PreflightInputs.HealthyTarget());

        var finding = findings.Only(WindowsPreflightFindingCodes.SourceRootUnusable);

        Assert.Equal(Severity.Error, finding.Severity);

        // The source root is not an entry, so the finding belongs to no relative path. Inventing one
        // would report an entry that was never observed.
        Assert.Null(finding.SourceRelativePath);

        // Only the root rule can say anything here: there is no inventory for any source-dependent rule
        // to reason about, and nothing about the target became a problem.
        Assert.DoesNotContain(findings, other => other.Severity == Severity.Error && other != finding);
    }

    [Fact]
    public void AMissingRootSaysSoWithoutClaimingAnythingElse()
    {
        var finding = PreflightInputs
            .Evaluate(PreflightInputs.UnusableRootSource(InvalidSourceRootReason.NotFound), PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceRootUnusable);

        Assert.Contains("does not exist", TextOf(finding));
    }

    [Fact]
    public void AFileAsTheRootIsNotDescribedAsAMissingDirectory()
    {
        // The scanner established that something is there and that it is not a directory. Calling that
        // "does not exist" would state a fact the scanner never established.
        var finding = PreflightInputs
            .Evaluate(
                PreflightInputs.UnusableRootSource(InvalidSourceRootReason.NotADirectory),
                PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceRootUnusable);

        var text = TextOf(finding);

        Assert.Contains("not a directory", text);
        Assert.DoesNotContain("does not exist", text);
    }

    [Theory]
    [InlineData(InvalidSourceRootReason.NotFound)]
    [InlineData(InvalidSourceRootReason.NotADirectory)]
    public void TheWordingNeverNamesTheInternalException(InvalidSourceRootReason reason)
    {
        var finding = PreflightInputs
            .Evaluate(PreflightInputs.UnusableRootSource(reason), PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.SourceRootUnusable);

        var text = TextOf(finding);

        Assert.DoesNotContain("InvalidSourceRootException", text);
        Assert.DoesNotContain("Exception", text);
        Assert.DoesNotContain("CopyPreflight", text);
        Assert.DoesNotContain("System.", text);

        // Nor may it promise anything about a copy; the most this check ever says about copying is that
        // no known blocking issue was detected.
        Assert.DoesNotContain("will fail", text);
    }

    [Fact]
    public void AUsableRootProducesNoRootFinding()
    {
        var findings = PreflightInputs.Evaluate(
            PreflightInputs.Source(SourceEntry.File("a.txt", 1)),
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAtDestinations(PreflightInputs.Source(SourceEntry.File("a.txt", 1))));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.SourceRootUnusable));
    }

    [Fact]
    public void AnUnusableRootIsEstablishedThroughTheEvaluatorAndNotOnlyTheRule()
    {
        // The evaluator is what the workflow calls, so the fact has to survive that entry point too.
        var findings = PreflightInputs.Evaluate(
            PreflightInputs.UnusableRootSource(),
            PreflightInputs.HealthyTarget(),
            PreflightInputs.FoundNothingAt());

        Assert.Equal(Severity.Error, findings.Only(WindowsPreflightFindingCodes.SourceRootUnusable).Severity);
    }

    /// <summary>Every user-facing text of a finding, for the wording assertions.</summary>
    private static string TextOf(Finding finding)
        => string.Join('\n', finding.Title, finding.Explanation, finding.Impact, finding.Recommendation);
}

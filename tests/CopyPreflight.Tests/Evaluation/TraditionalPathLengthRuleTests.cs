using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The classic <c>MAX_PATH</c> compatibility warning, which is a warning and never a verdict.
/// </summary>
public class TraditionalPathLengthRuleTests
{
    [Fact]
    public void AnOrdinaryDestinationPathIsNotReported()
    {
        var source = PreflightInputs.Source(SourceEntry.File(@"docs\report.docx", 10));

        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget());

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
    }

    [Theory]
    // The destination is the target root, one separator, then the relative path.
    [InlineData(249, 259, false)]
    [InlineData(250, 260, true)]
    [InlineData(251, 261, true)]
    [InlineData(299, 309, true)]
    public void TheFileLimitStartsAtTwoHundredAndSixtyCharacters(
        int relativePathLength,
        int expectedDestinationLength,
        bool expectWarning)
    {
        var relativePath = new string('p', relativePathLength);
        var destinationPath = Path.Join(PreflightInputs.TargetRoot, relativePath);

        // Fixture guard: the case really does compose the path length it claims to.
        Assert.Equal(expectedDestinationLength, destinationPath.Length);

        var source = PreflightInputs.Source(SourceEntry.File(relativePath, 1));
        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk);

        if (expectWarning)
        {
            var finding = Assert.Single(findings);

            // A path length is a compatibility risk, never an established failure.
            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal(relativePath, finding.SourceRelativePath);
            Assert.Contains(destinationPath, finding.Explanation, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    // Classic CreateDirectory must still be able to append an 8.3 name, so an ordinary directory
    // path is limited to MAX_PATH - 12 = 248 characters rather than to MAX_PATH itself.
    [InlineData(237, 247, false)]
    [InlineData(238, 248, false)]
    [InlineData(239, 249, true)]
    [InlineData(250, 260, true)]
    public void TheDirectoryLimitStartsAtTwoHundredAndFortyEightCharacters(
        int relativePathLength,
        int expectedDestinationLength,
        bool expectWarning)
    {
        var relativePath = new string('d', relativePathLength);
        var destinationPath = Path.Join(PreflightInputs.TargetRoot, relativePath);

        Assert.Equal(expectedDestinationLength, destinationPath.Length);

        var source = PreflightInputs.Source(SourceEntry.Directory(relativePath));
        var findings = PreflightInputs.Evaluate(source, PreflightInputs.HealthyTarget())
            .WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk);

        if (expectWarning)
        {
            var finding = Assert.Single(findings);

            Assert.Equal(Severity.Warning, finding.Severity);
            Assert.Equal(relativePath, finding.SourceRelativePath);
            Assert.Contains(destinationPath, finding.Explanation, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void ADirectoryIsJudgedByItsOwnShorterLimitRatherThanTheFileLimit()
    {
        // A directory path between 249 and 259 characters is already a classic directory-creation
        // risk, while a file of the same path length is still within the general MAX_PATH limit.
        var relativePath = new string('d', 245);
        var destinationPath = Path.Join(PreflightInputs.TargetRoot, relativePath);

        Assert.Equal(255, destinationPath.Length);

        var directoryFindings = PreflightInputs.Evaluate(
            PreflightInputs.Source(SourceEntry.Directory(relativePath)),
            PreflightInputs.HealthyTarget());

        var fileFindings = PreflightInputs.Evaluate(
            PreflightInputs.Source(SourceEntry.File(relativePath, 1)),
            PreflightInputs.HealthyTarget());

        Assert.Single(directoryFindings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
        Assert.Empty(fileFindings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
    }

    [Theory]
    // A reparse point's shape does not decide how it would be recreated, so it keeps the general
    // limit and is never handed a directory-creation conclusion the model cannot support.
    [InlineData(249, 259, false)]
    [InlineData(250, 260, true)]
    public void AReparsePointShapedLikeADirectoryKeepsTheGeneralLimit(
        int relativePathLength,
        int expectedDestinationLength,
        bool expectWarning)
    {
        var relativePath = new string('r', relativePathLength);
        var destinationPath = Path.Join(PreflightInputs.TargetRoot, relativePath);

        Assert.Equal(expectedDestinationLength, destinationPath.Length);

        var findings = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.ReparsePoint(relativePath, SourceEntryKind.Directory)),
                PreflightInputs.HealthyTarget())
            .WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk);

        Assert.Equal(expectWarning ? 1 : 0, findings.Count);
        Assert.All(findings, finding => Assert.Equal(Severity.Warning, finding.Severity));
    }

    [Fact]
    public void TheFindingNeverClaimsThatTheCopyIsCertainToFail()
    {
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.File(@"deep\" + new string('p', 260), 1)),
                PreflightInputs.HealthyTarget())
            .Only(WindowsPreflightFindingCodes.TraditionalPathLengthRisk);

        var text = $"{finding.Explanation} {finding.Impact} {finding.Recommendation}";

        Assert.DoesNotContain("will fail", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot be copied", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed", text, StringComparison.OrdinalIgnoreCase);

        // The reader is told what the outcome actually depends on, and that long path support exists.
        Assert.Contains("long-path aware", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1607", text, StringComparison.Ordinal);
        Assert.Contains("depends", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\\?\C:\")]
    [InlineData(@"\\?\UNC\server\share\")]
    // The prefix itself is case-insensitive, so the UNC form is recognised in either casing.
    [InlineData(@"\\?\unc\server\share\")]
    public void APathInTheExtendedLengthNamespaceIsNotJudgedByTheClassicLimit(string targetPrefix)
    {
        // A path rooted in the extended-length file namespace is passed on without classic path
        // handling, so the MAX_PATH heuristics do not describe it.
        var targetDirectoryPath = targetPrefix + new string('d', 300);
        var relativePath = "a.txt";
        var destinationPath = Path.Join(targetDirectoryPath, relativePath);

        // Fixture guard: without the namespace prefix this path would have been reported.
        Assert.True(destinationPath.Length >= 260, "the fixture must compose a path beyond MAX_PATH");

        var source = PreflightInputs.Source(SourceEntry.File(relativePath, 1));
        var target = PreflightInputs.HealthyTarget(targetDirectoryPath);

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
    }

    [Theory]
    [InlineData(@"\\.\C:\")]
    [InlineData(@"\\.\")]
    public void TheDeviceNamespaceIsNotALongPathExemption(string targetPrefix)
    {
        // \\.\  is the Win32 device namespace, which exists for device access. It is not the
        // extended-length file prefix, so it does not exempt a path from classic MAX_PATH handling.
        var targetDirectoryPath = targetPrefix + new string('d', 300);
        var relativePath = "a.txt";
        var destinationPath = Path.Join(targetDirectoryPath, relativePath);

        Assert.True(destinationPath.Length >= 260, "the fixture must compose a path beyond MAX_PATH");

        var source = PreflightInputs.Source(SourceEntry.File(relativePath, 1));
        var target = PreflightInputs.HealthyTarget(targetDirectoryPath);

        var finding = PreflightInputs.Evaluate(source, target)
            .Only(WindowsPreflightFindingCodes.TraditionalPathLengthRisk);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains(destinationPath, finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamespaceIsReadFromTheComposedDestinationPath()
    {
        // A plain target root produces a plain destination path, so the warning still applies even
        // when the source structure is what makes the path long.
        var target = PreflightInputs.HealthyTarget(@"C:\" + new string('t', 300));
        var source = PreflightInputs.Source(SourceEntry.File("a.txt", 1));

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
    }

    [Fact]
    public void APathOfExactlyTheLimitIsNotTreatedAsSafelyWithinIt()
    {
        // MAX_PATH counts the terminating null, so a 260 character path already has no room for one.
        var relativePath = new string('p', 250);
        var destinationPath = Path.Join(PreflightInputs.TargetRoot, relativePath);

        Assert.Equal(260, destinationPath.Length);

        var findings = PreflightInputs.Evaluate(
            PreflightInputs.Source(SourceEntry.File(relativePath, 1)),
            PreflightInputs.HealthyTarget());

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.TraditionalPathLengthRisk));
    }
}

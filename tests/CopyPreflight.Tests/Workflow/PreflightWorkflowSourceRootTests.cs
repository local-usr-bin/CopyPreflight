using System.IO;
using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// A source root that cannot be used: an expected outcome of the input, reported as a source-side
/// conclusion instead of as a failure of the application.
/// </summary>
/// <remarks>
/// <para>
/// These runs are real. The root is a path inside this test's own fixture tree that the test simply
/// does not create, so the scanner reaches its own decision through the real file system, and the run
/// goes through the real workflow, the real target probes and the real rule layer.
/// </para>
/// <para>
/// The permission case cannot be produced honestly without changing access control on the machine, so
/// the scripted reader the scanner already exposes is used for it. Everything else about that run is
/// real.
/// </para>
/// </remarks>
public class PreflightWorkflowSourceRootTests
{
    [Fact]
    public async Task RunAsync_WhenTheSourceRootDoesNotExist_ReportsAStructuredSourceError()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var missingRoot = source.PathOf("does-not-exist");

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(missingRoot, target.Root));

        // The input was refused, but the run itself completed: the refusal is a fact about the source,
        // and the application ran perfectly.
        Assert.Equal(PreflightRunStatus.Completed, result.Status);
        Assert.Null(result.Failure);
        Assert.Null(result.StoppedInPhase);

        // The fact is carried as a source outcome, not as an inventory: no entry and no issue, but the
        // reason the root could not be used, and the resolved path it was decided on.
        var scan = Assert.IsType<SourceScanResult>(result.SourceScan);
        Assert.Equal(InvalidSourceRootReason.NotFound, scan.UnusableRootReason);
        Assert.False(scan.HasUsableSourceRoot);
        Assert.False(scan.IsComplete);
        Assert.Empty(scan.Entries);
        Assert.Empty(scan.Issues);
        Assert.Equal(Path.GetFullPath(missingRoot), scan.SourceRoot);

        // And it reaches the user as a structured, source-side blocking finding.
        var finding = result.Findings.Only(WindowsPreflightFindingCodes.SourceRootUnusable);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Null(finding.SourceRelativePath);
        Assert.Contains("does not exist", finding.Title);

        // Nothing on the way to that finding may expose the internal type that carries the refusal.
        foreach (var text in new[] { finding.Title, finding.Explanation, finding.Impact, finding.Recommendation })
        {
            Assert.DoesNotContain("Exception", text);
            Assert.DoesNotContain("CopyPreflight", text);
        }

        // The run states the source as incomplete rather than as never attempted, and it says why.
        Assert.Equal(PreflightPhaseCoverage.Incomplete, result.Coverage.SourceScan);
        Assert.True(result.HasCoverageGaps);

        // The target side was still established, exactly as it is for a target that Windows refuses, so
        // no fact that could be observed was dropped.
        Assert.NotNull(result.Target);
        Assert.NotNull(result.ExistingObjects);
        Assert.Equal(PreflightPhaseCoverage.Complete, result.Coverage.TargetCapabilities);

        // Nothing was created to make the gap go away.
        Assert.False(Directory.Exists(missingRoot));
    }

    [Fact]
    public async Task RunAsync_WhenTheSourcePathIsAFile_ReportsItAsNotADirectory()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var aFile = source.CreateFile("not-a-directory.txt", 1);

        var result = await new PreflightWorkflow().RunAsync(WorkflowInputs.Request(aFile, target.Root));

        var scan = Assert.IsType<SourceScanResult>(result.SourceScan);
        Assert.Equal(InvalidSourceRootReason.NotADirectory, scan.UnusableRootReason);

        var finding = result.Findings.Only(WindowsPreflightFindingCodes.SourceRootUnusable);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("not a directory", finding.Title);

        // The file is there. Saying it "does not exist" would be the opposite of what was established.
        Assert.DoesNotContain("does not exist", finding.Explanation);

        // The file itself is untouched.
        Assert.True(File.Exists(aFile));
    }

    [Fact]
    public async Task RunAsync_WhenTheSourceRootCannotBeListed_KeepsTheEnumerationFailureItIs()
    {
        // A root that is there but cannot be read is a different fact from a root that is not there.
        // It stays the failure the scanner already records for it, and it is never restated as absence.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var reader = new ScriptedDirectoryReader()
            .WithFailure(source.Root, new UnauthorizedAccessException("The test denies this listing."));

        var result = await WorkflowInputs.Over(new SourceScanner(reader))
            .RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Completed, result.Status);

        var scan = Assert.IsType<SourceScanResult>(result.SourceScan);
        Assert.True(scan.HasUsableSourceRoot, "A root that cannot be listed is still a usable root.");
        Assert.False(scan.IsComplete);

        var failure = result.Findings.Only(WindowsPreflightFindingCodes.SourceDirectoryEnumerationFailed);
        Assert.Equal(Severity.Error, failure.Severity);
        Assert.Null(failure.SourceRelativePath);

        // Neither absence nor the root condition itself may be claimed for a listing failure.
        Assert.DoesNotContain(result.Findings, finding => finding.Code == WindowsPreflightFindingCodes.SourceRootUnusable);
        Assert.DoesNotContain("does not exist", failure.Explanation);
    }

    [Theory]
    [InlineData("System.InvalidOperationException")]
    [InlineData("System.NotSupportedException")]
    [InlineData("System.ArgumentException")]
    public async Task RunAsync_WhenAStageThrowsSomethingElse_StillFails(string expectedExceptionType)
    {
        // Handling an unusable root as a fact must not become a way of swallowing defects. Only the
        // scanner's own precondition failure is an input condition; anything else still stops the run.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var thrown = (Exception)(expectedExceptionType switch
        {
            "System.NotSupportedException" => new NotSupportedException("The test made this stage break."),
            "System.ArgumentException" => new ArgumentException("The test made this stage break."),
            _ => new InvalidOperationException("The test made this stage break."),
        });

        var result = await WorkflowInputs.Over(new SourceScanner(new FlakyDirectoryReader(thrown)))
            .RunAsync(WorkflowInputs.Request(source.Root, target.Root));

        Assert.Equal(PreflightRunStatus.Failed, result.Status);
        Assert.Equal(PreflightRunPhase.ScanningSource, result.StoppedInPhase);
        Assert.Equal(expectedExceptionType, Assert.IsType<PreflightRunFailure>(result.Failure).ExceptionTypeName);

        // A failure is not dressed up as a source conclusion.
        Assert.Null(result.SourceScan);
        Assert.Empty(result.Findings);
        Assert.Empty(result.Findings.WithCode(WindowsPreflightFindingCodes.SourceRootUnusable));
    }

    [Fact]
    public async Task RunAsync_AfterTheSourceRootIsCreated_CompletesWithoutTheRootFailure()
    {
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var missingRoot = source.PathOf("later");
        var workflow = new PreflightWorkflow();

        var refused = await workflow.RunAsync(WorkflowInputs.Request(missingRoot, target.Root));

        Assert.Equal(Severity.Error, refused.Findings.Only(WindowsPreflightFindingCodes.SourceRootUnusable).Severity);

        // The user fixes the path; the next run is a new run, with nothing carried over from the last.
        Directory.CreateDirectory(missingRoot);
        File.WriteAllBytes(Path.Combine(missingRoot, "kept.txt"), new byte[4]);

        var second = await workflow.RunAsync(WorkflowInputs.Request(missingRoot, target.Root));

        Assert.Equal(PreflightRunStatus.Completed, second.Status);
        Assert.NotEqual(refused.Request.RequestId, second.Request.RequestId);

        var scan = Assert.IsType<SourceScanResult>(second.SourceScan);
        Assert.True(scan.HasUsableSourceRoot);
        Assert.True(scan.IsComplete);
        Assert.Equal(1L, scan.FileCount);

        Assert.Empty(second.Findings.WithCode(WindowsPreflightFindingCodes.SourceRootUnusable));
        Assert.DoesNotContain(second.Findings, finding => finding.Severity == Severity.Error);
    }

    [Fact]
    public async Task RunAsync_WhenTheRootIsUnusable_ProducesNoFindingFromTheMissingInventory()
    {
        // With nothing observed, no rule may conclude anything about source contents: not a name
        // problem, not a length problem, not a collision. The source-dependent rules receive an empty
        // entry list and therefore say nothing, rather than being handed fabricated entries.
        using var source = new TemporarySourceTree();
        using var target = new TemporarySourceTree();

        var result = await new PreflightWorkflow().RunAsync(
            WorkflowInputs.Request(source.PathOf("not-there"), target.Root));

        var codes = result.Findings.Select(finding => finding.Code.Value).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("SOURCE_ROOT_UNUSABLE", codes);
        Assert.DoesNotContain("SOURCE_ENTRY_METADATA_FAILED", codes);
        Assert.DoesNotContain("SOURCE_DIRECTORY_ENUMERATION_FAILED", codes);
        Assert.DoesNotContain("SOURCE_CASE_COLLISION_RISK", codes);
        Assert.DoesNotContain("REPARSE_POINT_PRESENT", codes);
        Assert.DoesNotContain("TRADITIONAL_PATH_LENGTH_RISK", codes);
        Assert.DoesNotContain("TARGET_COMPONENT_TOO_LONG", codes);
        Assert.DoesNotContain("TARGET_SPACE_MAY_BE_INSUFFICIENT", codes);
        Assert.DoesNotContain("TARGET_EXISTING_FILE_COLLISION", codes);
        Assert.DoesNotContain("TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE", codes);
    }
}

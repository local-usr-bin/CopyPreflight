using CopyPreflight.Core;
using CopyPreflight.Windows;

namespace CopyPreflight.Tests;

/// <summary>
/// The FAT32 single-file ceiling, including the exact byte at which it stops being representable.
/// </summary>
public class Fat32MaxFileSizeRuleTests
{
    /// <summary>The largest file a FAT32 32-bit byte count can describe: 4 GiB minus one byte.</summary>
    private const long LargestRepresentableFile = uint.MaxValue;

    [Theory]
    [InlineData("FAT32")]
    [InlineData("fat32")]
    [InlineData("Fat32")]
    [InlineData("fAt32")]
    public void OnlyFat32TargetsAreMatched_AndTheNameIsCaseInsensitive(string reportedName)
    {
        var source = PreflightInputs.Source(SourceEntry.File("big.iso", LargestRepresentableFile + 1));

        var findings = PreflightInputs.Evaluate(source, Fat32Target(reportedName));

        Assert.Single(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
    }

    [Theory]
    [InlineData("NTFS")]
    [InlineData("exFAT")]
    [InlineData("ReFS")]
    [InlineData("CDFS")]
    public void OtherFileSystemsAreNotJudgedByTheFat32Limit(string reportedName)
    {
        var source = PreflightInputs.Source(SourceEntry.File("big.iso", 64L * 1024 * 1024 * 1024));

        var findings = PreflightInputs.Evaluate(source, Fat32Target(reportedName));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
    }

    [Fact]
    public void AnUnknownFileSystemIsNeverAssumedToBeFat32()
    {
        var source = PreflightInputs.Source(SourceEntry.File("big.iso", 64L * 1024 * 1024 * 1024));
        var target = PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Unknown(),
            CapabilityValue<long>.Known(long.MaxValue),
            CapabilityValue<int>.Known(255));

        var findings = PreflightInputs.Evaluate(source, target);

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
    }

    [Theory]
    // The boundary is exact: the last representable size passes, one byte more does not.
    [InlineData(0L, false)]
    [InlineData(1L, false)]
    [InlineData(LargestRepresentableFile - 1, false)]
    [InlineData(LargestRepresentableFile, false)]
    [InlineData(LargestRepresentableFile + 1, true)]
    [InlineData(LargestRepresentableFile + 2, true)]
    [InlineData(8L * 1024 * 1024 * 1024, true)]
    public void TheCeilingIsTheLastRepresentableByteCount(long length, bool expectedFinding)
    {
        var source = PreflightInputs.Source(SourceEntry.File("file.bin", length));

        var findings = PreflightInputs.Evaluate(source, Fat32Target("FAT32"))
            .WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge);

        if (expectedFinding)
        {
            var finding = Assert.Single(findings);

            Assert.Equal(Severity.Error, finding.Severity);
            Assert.Equal("file.bin", finding.SourceRelativePath);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void ReparsePointsAreNeverJudgedByTheFat32Limit()
    {
        // A reparse point has no attributed length, so there is nothing to compare. Guessing one,
        // or treating the absent length as zero, would both state a fact the scanner never observed.
        var source = PreflightInputs.Source(
            SourceEntry.ReparsePoint(@"links\big.iso", SourceEntryKind.File),
            SourceEntry.ReparsePoint(@"links\bigdir", SourceEntryKind.Directory));

        var findings = PreflightInputs.Evaluate(source, Fat32Target("FAT32"));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
    }

    [Fact]
    public void DirectoriesAreNeverJudgedByTheFat32Limit()
    {
        var source = PreflightInputs.Source(SourceEntry.Directory("folder"));

        var findings = PreflightInputs.Evaluate(source, Fat32Target("FAT32"));

        Assert.Empty(findings.WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge));
    }

    [Fact]
    public void EveryOversizedFileIsReportedSeparately()
    {
        var source = PreflightInputs.Source(
            SourceEntry.File(@"media\one.iso", LargestRepresentableFile + 1),
            SourceEntry.File(@"media\two.iso", 8L * 1024 * 1024 * 1024),
            SourceEntry.File(@"media\small.txt", 1024));

        var findings = PreflightInputs.Evaluate(source, Fat32Target("FAT32"))
            .WithCode(WindowsPreflightFindingCodes.Fat32FileTooLarge);

        Assert.Equal(2, findings.Count);
        Assert.Equal(
            new[] { @"media\one.iso", @"media\two.iso" },
            findings.Select(finding => finding.SourceRelativePath).OrderBy(path => path, StringComparer.Ordinal));
    }

    [Fact]
    public void TheFindingPointsAtAlternativesWithoutPrescribingAFormat()
    {
        var finding = PreflightInputs.Evaluate(
                PreflightInputs.Source(SourceEntry.File("big.iso", LargestRepresentableFile + 1)),
                Fat32Target("FAT32"))
            .Only(WindowsPreflightFindingCodes.Fat32FileTooLarge);

        // Reformatting destroys what is already on the volume, so the advice has to say so.
        Assert.Contains("back", finding.Recommendation, StringComparison.OrdinalIgnoreCase);

        // The alternatives are offered, never presented as universally suitable or as mandatory.
        Assert.Contains("exFAT", finding.Recommendation, StringComparison.Ordinal);
        Assert.Contains("NTFS", finding.Recommendation, StringComparison.Ordinal);

        var text = $"{finding.Explanation} {finding.Impact} {finding.Recommendation}";
        Assert.DoesNotContain("must be formatted", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automatically", text, StringComparison.OrdinalIgnoreCase);
    }

    private static TargetProbeResult Fat32Target(string reportedName)
        => PreflightInputs.Target(
            CapabilityValue<FileSystemName>.Known(new FileSystemName(reportedName)),
            CapabilityValue<long>.Known(long.MaxValue),
            CapabilityValue<int>.Known(255));
}
